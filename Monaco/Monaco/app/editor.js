// Monaco bootstrap for the Toybox script editor.
//
// Tabs, the title bar, and the status bar are Avalonia chrome — this layer only ever shows ONE active model
// at a time and is told which document to display. Models are cached per file path so switching tabs (driven
// from the host) preserves each document's undo stack, cursor, and scroll position. The same page backs both
// the inline inspector strip and the popped-out dockable window; only the host-supplied options differ.
(function () {
  "use strict";

  var bridge = window.__tbx;

  // Same-origin worker (served from the vhost root); avoids the cross-origin blob-worker dance.
  self.MonacoEnvironment = {
    getWorkerUrl: function () { return "vs/base/worker/workerMain.js"; }
  };

  require.config({ paths: { vs: "vs" } });

  require(["vs/editor/editor.main"], function () {
    var boot = document.getElementById("boot");
    if (boot) boot.remove();

    // Register custom languages (e.g. GLSL) not in the vendored basic-languages bundle, before any model is
    // created, so a model opened as one of them already has its grammar.
    if (window.__tbxLanguages) window.__tbxLanguages.register(monaco);

    // Register GLSL #include navigation (a definition provider that resolves includes through the host).
    if (window.__tbxGlsl) window.__tbxGlsl.register(monaco);

    // Register the editor's colour themes (the editable type-colouring palette), before the editor is created
    // so the initial theme is ours.
    if (window.__tbxThemes) window.__tbxThemes.register(monaco);

    var models = Object.create(null);   // path -> { model, viewState }
    var activePath = null;
    var suppressChange = false;         // true while we apply host edits, so we don't echo them back

    var editor = monaco.editor.create(document.getElementById("root"), {
      value: "",
      language: "plaintext",
      theme: window.__tbxThemes ? window.__tbxThemes.dark : "vs-dark",
      automaticLayout: true,
      fontSize: 13,
      minimap: { enabled: true },
      scrollBeyondLastLine: false,
      smoothScrolling: true,
      tabSize: 4,
      renderWhitespace: "selection",
      fixedOverflowWidgets: true,
      // Render clangd's semantic tokens (types/members/params colour like an IDE), not just the grammar.
      "semanticHighlighting.enabled": true
    });

    // Fallback only: the C# host normally passes an explicit language id (resolved from ScriptLanguages) in the
    // open envelope, so this just covers a missing one. Keep the extensions in step with that table.
    function languageFor(path) {
      if (/\.(h|hpp|hxx|hh|inl|c|cc|cpp|cxx)$/i.test(path)) return "cpp";
      if (/\.(glsl|frag|vert|comp|geo)$/i.test(path)) return "glsl";
      if (/\.cs$/i.test(path)) return "csharp";
      if (/\.json$/i.test(path)) return "json";
      return "plaintext";
    }

    function rememberViewState() {
      if (activePath && models[activePath]) {
        models[activePath].viewState = editor.saveViewState();
      }
    }

    function show(path) {
      var entry = models[path];
      if (!entry) return;
      rememberViewState();
      activePath = path;
      suppressChange = true;
      editor.setModel(entry.model);
      suppressChange = false;
      if (entry.viewState) editor.restoreViewState(entry.viewState);
      editor.focus();
    }

    var lsp = window.__tbxLsp;
    if (lsp) lsp.attach(monaco);

    // Cross-file go-to-definition: when a definition (a C++/C# LSP result, or a resolved GLSL include) lands in
    // a DIFFERENT file, hand the open to the host so it opens a real tab; same-file jumps fall through to
    // Monaco's own in-place reveal. Only file:// targets (skip metadata/decompiled sources, which peek instead).
    if (monaco.editor.registerEditorOpener) {
      monaco.editor.registerEditorOpener({
        openCodeEditor: function (source, resource, selectionOrPosition) {
          if (resource.scheme !== "file") return false;
          var current = editor.getModel();
          if (current && resource.toString() === current.uri.toString()) return false;
          var line = 0;
          if (selectionOrPosition) {
            line = selectionOrPosition.startLineNumber || selectionOrPosition.lineNumber || 0;
          }
          bridge.post({ kind: "openFile", uri: resource.toString(), line: line });
          return true; // Handled out-of-band: the host opens the tab.
        }
      });
    }

    // host -> web: open or update a document, then make it active.
    bridge.on("open", function (m) {
      var entry = models[m.path];
      if (!entry) {
        // Forward-slash the path so the model URI (file:///c:/…) matches clangd's compile_commands entries.
        var uri = monaco.Uri.file(m.path.replace(/\\/g, "/"));
        var model = monaco.editor.createModel(m.text || "", m.language || languageFor(m.path), uri);
        entry = models[m.path] = { model: model, viewState: null };
        if (lsp) lsp.didOpen(model);
        model.onDidChangeContent(function () {
          if (lsp) lsp.didChange(model);
          if (suppressChange || activePath !== m.path) return;
          bridge.post({
            kind: "editor", type: "change",
            path: m.path, text: model.getValue(), version: model.getVersionId()
          });
        });
      } else if (typeof m.text === "string" && m.text !== entry.model.getValue()) {
        // Host pushed authoritative content (e.g. reloaded from disk) — replace without echoing.
        suppressChange = true;
        entry.model.setValue(m.text);
        suppressChange = false;
      }
      show(m.path);
    });

    bridge.on("setActive", function (m) { show(m.path); });

    // host -> web: scroll a document to a 1-based line and drop the caret there. Ensures the model is showing
    // first (an open + reveal arrive back-to-back when a log link opens a fresh file).
    bridge.on("reveal", function (m) {
      if (!models[m.path]) return;
      if (activePath !== m.path) show(m.path);
      var line = m.line > 0 ? m.line : 1;
      editor.revealLineInCenter(line);
      editor.setPosition({ lineNumber: line, column: 1 });
      editor.focus();
    });

    bridge.on("close", function (m) {
      var entry = models[m.path];
      if (!entry) return;
      if (lsp) lsp.didClose(entry.model);
      if (activePath === m.path) { activePath = null; editor.setModel(null); }
      entry.model.dispose();
      delete models[m.path];
    });

    bridge.on("options", function (m) {
      var opts = {};
      if (typeof m.fontSize === "number") opts.fontSize = m.fontSize;
      if (typeof m.minimap === "boolean") opts.minimap = { enabled: m.minimap };
      if (typeof m.readOnly === "boolean") opts.readOnly = m.readOnly;
      if (typeof m.lineNumbers === "string") opts.lineNumbers = m.lineNumbers;
      if (typeof m.wordWrap === "string") opts.wordWrap = m.wordWrap;
      editor.updateOptions(opts);
    });

    bridge.on("theme", function (m) {
      var themes = window.__tbxThemes;
      // Re-derive the editor accents (caret/selection/focus) from the app theme's primary before selecting the
      // light/dark base, so a theme switch recolours them too.
      if (themes && m.primary) themes.register(monaco, m.primary);
      if (m.base === "light")
        monaco.editor.setTheme(themes ? themes.light : "vs");
      else
        monaco.editor.setTheme(themes ? themes.dark : "vs-dark");
    });

    editor.onDidChangeCursorPosition(function (e) {
      bridge.post({ kind: "editor", type: "cursor", line: e.position.lineNumber, column: e.position.column });
    });

    // Ctrl+S is owned by the host (compile + hot-reload pipeline), not the browser.
    editor.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyS, function () {
      if (!activePath || !models[activePath]) return;
      bridge.post({ kind: "fs", type: "save", path: activePath, text: models[activePath].model.getValue() });
    });

    // Expose a hook the LSP layer (Phase 4) attaches to without re-reaching into this closure.
    window.__tbxEditor = {
      monaco: monaco,
      getEditor: function () { return editor; },
      getModel: function (path) { return models[path] ? models[path].model : null; }
    };

    bridge.post({ kind: "ready" });
  });
})();
