// A minimal multi-server LSP client that bridges the language servers spawned by the C# host (clangd for C++,
// Roslyn for C#, …) to Monaco's language providers, without pulling in the bundler-only monaco-languageclient.
//
// Each server is a separate CLIENT, keyed by the server id the host tags it with. A client stays dormant until
// the host signals its server is up (an "lspEnable" envelope carrying { server, languages, rootUri, … }); then
// it runs its own initialize handshake, sends any post-initialise notifications the host specified (e.g. Roslyn's
// solution/open), and registers Monaco providers for its languages. Wire format: LSP JSON-RPC messages travel as
// { kind:"lsp", server:<id>, message:<jsonrpc> } envelopes in both directions; responses correlate by id per
// client. editor.js calls didOpen/didChange/didClose as models come and go — each is fanned out to every client
// that serves that model's language.
(function () {
  "use strict";

  var bridge = window.__tbx;
  var monaco = null;
  var clients = Object.create(null);            // server id -> client
  var providerLanguages = Object.create(null);  // language id -> true (Monaco providers registered once)

  // ---- LSP <-> Monaco position helpers (LSP is 0-based, Monaco is 1-based) ----
  function toLspPos(position) { return { line: position.lineNumber - 1, character: position.column - 1 }; }
  function toMonacoRange(range) {
    return new monaco.Range(
      range.start.line + 1, range.start.character + 1,
      range.end.line + 1, range.end.character + 1);
  }

  // ---- per-client transport ----
  function send(client, message) {
    message.jsonrpc = "2.0";
    bridge.post({ kind: "lsp", server: client.id, message: message });
  }
  function request(client, method, params) {
    var id = client.nextId++;
    send(client, { id: id, method: method, params: params });
    return new Promise(function (resolve, reject) { client.pending[id] = { resolve: resolve, reject: reject }; });
  }
  function notify(client, method, params) { send(client, { method: method, params: params }); }

  function serves(client, languageId) { return client.languages.indexOf(languageId) !== -1; }
  function clientFor(languageId) {
    for (var id in clients) if (serves(clients[id], languageId)) return clients[id];
    return null;
  }
  function reportStatus(client, state) {
    bridge.post({ kind: "editor", type: "lspStatus", server: client.id, state: state });
  }

  function start(client) {
    if (client.started) return;
    client.started = true;
    request(client, "initialize", {
      processId: null,
      rootUri: client.rootUri,
      initializationOptions: client.initOptions || undefined,
      capabilities: {
        workspace: {
          configuration: true,
          didChangeWatchedFiles: { dynamicRegistration: true }
        },
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
      workspaceFolders: client.rootUri ? [{ uri: client.rootUri, name: "workspace" }] : null
    }).then(function (result) {
      notify(client, "initialized", {});
      // Post-initialise notifications the host asked for (e.g. Roslyn's solution/open — the server loads no
      // workspace from rootUri, so this is what actually triggers indexing).
      client.postInit.forEach(function (n) { notify(client, n.method, n.params || {}); });
      var provider = result && result.capabilities && result.capabilities.semanticTokensProvider;
      if (provider && provider.legend && monaco) registerSemanticTokens(client, provider.legend);
      // A server that loads a workspace asynchronously (has post-init steps, e.g. Roslyn) is only usefully
      // "ready" once it signals projectInitializationComplete; a synchronous one (clangd) is ready now.
      if (client.postInit.length) reportStatus(client, "starting");
      else { client.ready = true; reportStatus(client, "ready"); }
      flushOpenDocs(client);
    }, function () {
      reportStatus(client, "error");
    });
  }

  function flushOpenDocs(client) {
    if (!monaco) return;
    monaco.editor.getModels().forEach(function (model) { didOpenTo(client, model); });
  }

  // ---- Document sync (per client, only for languages it serves) ----
  function didOpenTo(client, model) {
    if (!client.started || !monaco || !serves(client, model.getLanguageId())) return;
    var uri = model.uri.toString();
    if (client.openDocs[uri] !== undefined) return;
    client.openDocs[uri] = 1;
    notify(client, "textDocument/didOpen", {
      textDocument: { uri: uri, languageId: model.getLanguageId(), version: 1, text: model.getValue() }
    });
  }

  function didChangeTo(client, model) {
    if (!client.started || !serves(client, model.getLanguageId())) return;
    var uri = model.uri.toString();
    if (client.openDocs[uri] === undefined) { didOpenTo(client, model); return; }
    var version = ++client.openDocs[uri];
    notify(client, "textDocument/didChange", {
      textDocument: { uri: uri, version: version },
      contentChanges: [{ text: model.getValue() }]
    });
  }

  function didCloseTo(client, model) {
    if (!client.started) return;
    var uri = model.uri.toString();
    if (client.openDocs[uri] === undefined) return;
    delete client.openDocs[uri];
    notify(client, "textDocument/didClose", { textDocument: { uri: uri } });
  }

  function eachClient(fn) { for (var id in clients) fn(clients[id]); }
  function didOpen(model) { eachClient(function (c) { didOpenTo(c, model); }); }
  function didChange(model) { eachClient(function (c) { didChangeTo(c, model); }); }
  function didClose(model) { eachClient(function (c) { didCloseTo(c, model); }); }

  // ---- Inbound dispatch (server -> client), routed by envelope.server ----
  bridge.on("lsp", function (envelope) {
    var client = clients[envelope.server];
    var message = envelope.message;
    if (!client || !message) return;

    if (message.id !== undefined && (message.result !== undefined || message.error !== undefined)) {
      var entry = client.pending[message.id];
      delete client.pending[message.id];
      if (!entry) return;
      if (message.error) entry.reject(message.error); else entry.resolve(message.result);
      return;
    }

    // Server -> client request (has both id and method): must be answered or the server waits.
    if (message.id !== undefined && message.method) {
      handleServerRequest(client, message);
      return;
    }

    if (message.method === "textDocument/publishDiagnostics") applyDiagnostics(client, message.params);
    else if (message.method === "workspace/projectInitializationComplete") { client.ready = true; reportStatus(client, "ready"); }
  });

  function handleServerRequest(client, message) {
    switch (message.method) {
      // clangd fires this once its index is ready to say "re-request semantic tokens, I have them now".
      case "workspace/semanticTokens/refresh":
        if (client.semanticTokensEmitter) client.semanticTokensEmitter.fire();
        send(client, { id: message.id, result: null });
        return;
      // Roslyn dynamically registers capabilities/watchers and asks for configuration + progress hosts. Answer
      // benignly so it doesn't block: acknowledge registrations/refreshes, and hand back nulls for config.
      case "client/registerCapability":
      case "client/unregisterCapability":
      case "window/workDoneProgress/create":
      case "workspace/diagnostic/refresh":
      case "workspace/inlayHint/refresh":
      case "workspace/codeLens/refresh":
        send(client, { id: message.id, result: null });
        return;
      case "workspace/configuration":
        var items = (message.params && message.params.items) || [];
        send(client, { id: message.id, result: items.map(function () { return null; }) });
        return;
      default:
        send(client, { id: message.id, error: { code: -32601, message: "method not found" } });
        return;
    }
  }

  function applyDiagnostics(client, params) {
    if (!monaco) return;
    var model = monaco.editor.getModel(monaco.Uri.parse(params.uri));
    if (!model) return;
    var markers = (params.diagnostics || []).map(function (d) {
      return {
        severity: severityFor(d.severity),
        message: d.message,
        source: d.source || client.id,
        startLineNumber: d.range.start.line + 1,
        startColumn: d.range.start.character + 1,
        endLineNumber: d.range.end.line + 1,
        endColumn: d.range.end.character + 1
      };
    });
    monaco.editor.setModelMarkers(model, client.id, markers);
  }

  function severityFor(s) {
    switch (s) {
      case 1: return monaco.MarkerSeverity.Error;
      case 2: return monaco.MarkerSeverity.Warning;
      case 3: return monaco.MarkerSeverity.Info;
      default: return monaco.MarkerSeverity.Hint;
    }
  }

  // ---- Monaco language providers ----
  // Registered ONCE per language id; each provider looks up the client that serves the language at call time, so
  // several servers coexist and a language never has two competing providers.
  function registerProvidersFor(languageId) {
    if (providerLanguages[languageId]) return;
    providerLanguages[languageId] = true;

    monaco.languages.registerCompletionItemProvider(languageId, {
      triggerCharacters: [".", ">", ":", "<", "\"", "/", " "],
      provideCompletionItems: function (model, position) {
        var client = clientFor(model.getLanguageId());
        if (!client) return { suggestions: [] };
        return request(client, "textDocument/completion", {
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

    monaco.languages.registerHoverProvider(languageId, {
      provideHover: function (model, position) {
        var client = clientFor(model.getLanguageId());
        if (!client) return null;
        return request(client, "textDocument/hover", {
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

    monaco.languages.registerDefinitionProvider(languageId, {
      provideDefinition: function (model, position) {
        var client = clientFor(model.getLanguageId());
        if (!client) return null;
        return request(client, "textDocument/definition", {
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

    monaco.languages.registerSignatureHelpProvider(languageId, {
      signatureHelpTriggerCharacters: ["(", ","],
      provideSignatureHelp: function (model, position) {
        var client = clientFor(model.getLanguageId());
        if (!client) return null;
        return request(client, "textDocument/signatureHelp", {
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

  // Semantic tokens are per client (each server has its own legend). The token data is already in Monaco's
  // relative 5-int encoding, so it passes straight through; the legend maps indices to type/modifier names.
  function registerSemanticTokens(client, legend) {
    client.semanticTokensEmitter = new monaco.Emitter();
    var provider = {
      onDidChange: client.semanticTokensEmitter.event,
      getLegend: function () {
        return { tokenTypes: legend.tokenTypes || [], tokenModifiers: legend.tokenModifiers || [] };
      },
      provideDocumentSemanticTokens: function (model) {
        var owner = clientFor(model.getLanguageId());
        if (owner !== client) return null;
        return request(client, "textDocument/semanticTokens/full", { textDocument: { uri: model.uri.toString() } })
          .then(function (result) {
            if (!result || !result.data) return null;
            return { data: new Uint32Array(result.data), resultId: result.resultId };
          }, function () { return null; });
      },
      releaseDocumentSemanticTokens: function () {}
    };
    client.languages.forEach(function (languageId) {
      monaco.languages.registerDocumentSemanticTokensProvider(languageId, provider);
    });
  }

  function completionKind(kind) {
    var Kind = monaco.languages.CompletionItemKind;
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

  // Bring a client fully online: register its languages' providers (once each) and run the handshake.
  function activate(client) {
    if (!monaco) return;
    client.languages.forEach(registerProvidersFor);
    start(client);
  }

  // editor.js calls attach() once Monaco is ready, then the document hooks as models change.
  window.__tbxLsp = {
    attach: function (monacoApi) {
      monaco = monacoApi;
      eachClient(activate);
    },
    didOpen: didOpen,
    didChange: didChange,
    didClose: didClose
  };

  // Host turns a server's client on once its process is spawned.
  bridge.on("lspEnable", function (m) {
    if (clients[m.server]) return;
    clients[m.server] = {
      id: m.server,
      languages: m.languages || [],
      rootUri: m.rootUri || null,
      initOptions: m.initializationOptions || null,
      postInit: m.postInitialize || [],
      nextId: 1,
      pending: Object.create(null),
      openDocs: Object.create(null),
      started: false,
      ready: false,
      semanticTokensEmitter: null
    };
    activate(clients[m.server]);
  });
})();
