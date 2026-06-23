// A minimal LSP client that bridges clangd (spawned by the C# host) to Monaco's language providers, without
// pulling in the bundler-only monaco-languageclient. It stays dormant until the host signals that clangd is
// up (the "lspEnable" envelope) — so the inline inspector strip, which has no clangd behind it, never spins.
//
// Wire format: LSP JSON-RPC messages travel as { kind:"lsp", message:<jsonrpc> } envelopes in both
// directions. editor.js calls our didOpen/didChange/didClose as models come and go; we translate Monaco
// requests into LSP requests, correlate responses by id, and turn clangd's diagnostics into model markers.
(function () {
  "use strict";

  var bridge = window.__tbx;
  var monaco = null;
  var enabled = false;
  var initialized = false;
  var nextId = 1;
  var pending = Object.create(null);   // request id -> {resolve, reject}
  var openDocs = Object.create(null);  // uri -> version
  var rootUri = null;

  function send(message) {
    message.jsonrpc = "2.0";
    bridge.post({ kind: "lsp", message: message });
  }

  function request(method, params) {
    var id = nextId++;
    send({ id: id, method: method, params: params });
    return new Promise(function (resolve, reject) { pending[id] = { resolve: resolve, reject: reject }; });
  }

  function notify(method, params) { send({ method: method, params: params }); }

  // ---- LSP <-> Monaco position helpers (LSP is 0-based, Monaco is 1-based) ----
  function toLspPos(position) { return { line: position.lineNumber - 1, character: position.column - 1 }; }
  function toMonacoRange(range) {
    return new monaco.Range(
      range.start.line + 1, range.start.character + 1,
      range.end.line + 1, range.end.character + 1);
  }

  function start(root) {
    rootUri = root || null;
    if (initialized) return;
    initialized = true;
    request("initialize", {
      processId: null,
      rootUri: rootUri,
      capabilities: {
        textDocument: {
          synchronization: { didSave: true, dynamicRegistration: false },
          completion: { completionItem: { snippetSupport: true, documentationFormat: ["markdown", "plaintext"] } },
          hover: { contentFormat: ["markdown", "plaintext"] },
          definition: {},
          signatureHelp: {},
          publishDiagnostics: {},
          semanticTokens: {
            requests: { full: true, range: false },
            formats: ["relative"],
            tokenTypes: ["namespace", "type", "class", "enum", "interface", "struct", "typeParameter",
              "parameter", "variable", "property", "enumMember", "event", "function", "method", "macro",
              "keyword", "modifier", "comment", "string", "number", "regexp", "operator"],
            tokenModifiers: ["declaration", "definition", "readonly", "static", "deprecated", "abstract",
              "async", "modification", "documentation", "defaultLibrary"]
          }
        }
      },
      // clangd reads compile_commands.json relative to the workspace folder.
      workspaceFolders: rootUri ? [{ uri: rootUri, name: "project" }] : null
    }).then(function (result) {
      notify("initialized", {});
      // Register semantic highlighting using clangd's own token legend (its token data is already in Monaco's
      // relative format, so it passes through). This is what makes types/members/params colour like an IDE.
      var provider = result && result.capabilities && result.capabilities.semanticTokensProvider;
      if (provider && provider.legend && monaco) registerSemanticTokens(provider.legend);
      // Any models opened before initialize completes are flushed now.
      flushOpenDocs();
    });
  }

  function flushOpenDocs() {
    if (!monaco) return;
    monaco.editor.getModels().forEach(didOpen);
  }

  // ---- Document sync ----
  function didOpen(model) {
    if (!enabled || !initialized || !monaco) return;
    var uri = model.uri.toString();
    if (openDocs[uri] !== undefined) return;
    openDocs[uri] = 1;
    notify("textDocument/didOpen", {
      textDocument: { uri: uri, languageId: model.getLanguageId(), version: 1, text: model.getValue() }
    });
  }

  function didChange(model) {
    if (!enabled || !initialized) return;
    var uri = model.uri.toString();
    if (openDocs[uri] === undefined) { didOpen(model); return; }
    var version = ++openDocs[uri];
    // Full-document sync keeps the client tiny; clangd handles whole-file updates fine for editor-scale files.
    notify("textDocument/didChange", {
      textDocument: { uri: uri, version: version },
      contentChanges: [{ text: model.getValue() }]
    });
  }

  function didClose(model) {
    if (!enabled || !initialized) return;
    var uri = model.uri.toString();
    if (openDocs[uri] === undefined) return;
    delete openDocs[uri];
    notify("textDocument/didClose", { textDocument: { uri: uri } });
  }

  // ---- Inbound dispatch (server -> client) ----
  bridge.on("lsp", function (envelope) {
    var message = envelope.message;
    if (!message) return;

    if (message.id !== undefined && (message.result !== undefined || message.error !== undefined)) {
      var entry = pending[message.id];
      delete pending[message.id];
      if (!entry) return;
      if (message.error) entry.reject(message.error); else entry.resolve(message.result);
      return;
    }

    if (message.method === "textDocument/publishDiagnostics")
      applyDiagnostics(message.params);
    // Other server requests (e.g. workspace/configuration) are ignored; clangd tolerates the absence.
  });

  function applyDiagnostics(params) {
    if (!monaco) return;
    var model = monaco.editor.getModel(monaco.Uri.parse(params.uri));
    if (!model) return;
    var markers = (params.diagnostics || []).map(function (d) {
      return {
        severity: severityFor(d.severity),
        message: d.message,
        source: d.source || "clangd",
        startLineNumber: d.range.start.line + 1,
        startColumn: d.range.start.character + 1,
        endLineNumber: d.range.end.line + 1,
        endColumn: d.range.end.character + 1
      };
    });
    monaco.editor.setModelMarkers(model, "clangd", markers);
  }

  function severityFor(s) {
    switch (s) {
      case 1: return monaco.MarkerSeverity.Error;
      case 2: return monaco.MarkerSeverity.Warning;
      case 3: return monaco.MarkerSeverity.Info;
      default: return monaco.MarkerSeverity.Hint;
    }
  }

  // ---- Monaco language providers (client -> server requests) ----
  function registerProviders() {
    monaco.languages.registerCompletionItemProvider("cpp", {
      triggerCharacters: [".", ">", ":", "<", "\"", "/"],
      provideCompletionItems: function (model, position) {
        return request("textDocument/completion", {
          textDocument: { uri: model.uri.toString() }, position: toLspPos(position)
        }).then(function (result) {
          var items = (result && (result.items || result)) || [];
          var word = model.getWordUntilPosition(position);
          var range = new monaco.Range(position.lineNumber, word.startColumn, position.lineNumber, word.endColumn);
          return {
            suggestions: items.map(function (it) {
              return {
                label: it.label,
                kind: completionKind(it.kind),
                insertText: it.insertText || it.label,
                insertTextRules: it.insertTextFormat === 2
                  ? monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet : 0,
                detail: it.detail,
                documentation: docToMarkdown(it.documentation),
                sortText: it.sortText,
                filterText: it.filterText,
                range: range
              };
            })
          };
        }, function () { return { suggestions: [] }; });
      }
    });

    monaco.languages.registerHoverProvider("cpp", {
      provideHover: function (model, position) {
        return request("textDocument/hover", {
          textDocument: { uri: model.uri.toString() }, position: toLspPos(position)
        }).then(function (result) {
          if (!result || !result.contents) return null;
          return {
            range: result.range ? toMonacoRange(result.range) : undefined,
            contents: [{ value: contentsToMarkdown(result.contents) }]
          };
        }, function () { return null; });
      }
    });

    monaco.languages.registerDefinitionProvider("cpp", {
      provideDefinition: function (model, position) {
        return request("textDocument/definition", {
          textDocument: { uri: model.uri.toString() }, position: toLspPos(position)
        }).then(function (result) {
          if (!result) return null;
          var locations = Array.isArray(result) ? result : [result];
          return locations.map(function (loc) {
            return { uri: monaco.Uri.parse(loc.uri), range: toMonacoRange(loc.range || loc.targetRange) };
          });
        }, function () { return null; });
      }
    });

    monaco.languages.registerSignatureHelpProvider("cpp", {
      signatureHelpTriggerCharacters: ["(", ","],
      provideSignatureHelp: function (model, position) {
        return request("textDocument/signatureHelp", {
          textDocument: { uri: model.uri.toString() }, position: toLspPos(position)
        }).then(function (result) {
          if (!result || !result.signatures || !result.signatures.length) return null;
          return {
            value: {
              signatures: result.signatures.map(function (s) {
                return {
                  label: s.label,
                  documentation: docToMarkdown(s.documentation),
                  parameters: (s.parameters || []).map(function (p) {
                    return { label: p.label, documentation: docToMarkdown(p.documentation) };
                  })
                };
              }),
              activeSignature: result.activeSignature || 0,
              activeParameter: result.activeParameter || 0
            },
            dispose: function () {}
          };
        }, function () { return null; });
      }
    });
  }

  // Registered after initialize (needs clangd's legend). clangd emits full semantic tokens in the relative
  // 5-int encoding Monaco expects, so the data array passes straight through; the legend maps indices to the
  // type/modifier names the theme colours by.
  function registerSemanticTokens(legend) {
    monaco.languages.registerDocumentSemanticTokensProvider("cpp", {
      getLegend: function () {
        return { tokenTypes: legend.tokenTypes || [], tokenModifiers: legend.tokenModifiers || [] };
      },
      provideDocumentSemanticTokens: function (model) {
        return request("textDocument/semanticTokens/full", { textDocument: { uri: model.uri.toString() } })
          .then(function (result) {
            if (!result || !result.data) return null;
            return { data: new Uint32Array(result.data), resultId: result.resultId };
          }, function () { return null; });
      },
      releaseDocumentSemanticTokens: function () {}
    });
  }

  function completionKind(kind) {
    var Kind = monaco.languages.CompletionItemKind;
    // LSP CompletionItemKind -> Monaco (close-enough mapping for the common cases).
    var map = {
      1: Kind.Text, 2: Kind.Method, 3: Kind.Function, 4: Kind.Constructor, 5: Kind.Field,
      6: Kind.Variable, 7: Kind.Class, 8: Kind.Interface, 9: Kind.Module, 10: Kind.Property,
      11: Kind.Unit, 12: Kind.Value, 13: Kind.Enum, 14: Kind.Keyword, 15: Kind.Snippet,
      16: Kind.Color, 17: Kind.File, 18: Kind.Reference, 21: Kind.Constant, 22: Kind.Struct,
      23: Kind.Event, 25: Kind.TypeParameter
    };
    return map[kind] || Kind.Text;
  }

  function docToMarkdown(doc) {
    if (!doc) return undefined;
    return { value: typeof doc === "string" ? doc : (doc.value || "") };
  }

  function contentsToMarkdown(contents) {
    if (typeof contents === "string") return contents;
    if (Array.isArray(contents)) return contents.map(contentsToMarkdown).join("\n\n");
    if (contents.kind) return contents.value;             // MarkupContent
    if (contents.language) return "```" + contents.language + "\n" + contents.value + "\n```";  // MarkedString
    return contents.value || "";
  }

  // editor.js calls attach() once Monaco is ready, then the document hooks as models change.
  window.__tbxLsp = {
    attach: function (monacoApi) {
      monaco = monacoApi;
      if (enabled) registerProviders();
    },
    didOpen: didOpen,
    didChange: didChange,
    didClose: didClose
  };

  // Host turns the client on once clangd is spawned, passing the workspace root uri.
  bridge.on("lspEnable", function (m) {
    if (enabled) return;
    enabled = true;
    if (monaco) registerProviders();
    start(m.rootUri);
  });
})();
