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

    var models = Object.create(null);   // path -> { model, viewState }
    var activePath = null;
    var suppressChange = false;         // true while we apply host edits, so we don't echo them back

    var editor = monaco.editor.create(document.getElementById("root"), {
      value: "",
      language: "cpp",
      theme: "vs-dark",
      automaticLayout: true,
      fontSize: 13,
      minimap: { enabled: true },
      scrollBeyondLastLine: false,
      smoothScrolling: true,
      tabSize: 4,
      renderWhitespace: "selection",
      fixedOverflowWidgets: true
    });

    function languageFor(path) {
      if (/\.(h|hpp|hxx|hh|inl)$/i.test(path)) return "cpp";
      if (/\.(c|cc|cpp|cxx)$/i.test(path)) return "cpp";
      if (/\.json$/i.test(path)) return "json";
      return "cpp";
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
      monaco.editor.setTheme(m.base === "light" ? "vs" : "vs-dark");
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
