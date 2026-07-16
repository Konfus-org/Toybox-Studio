// GLSL include navigation: makes go-to-definition / Ctrl-click on an `#include` at the top of a shader jump to
// the included file. GLSL has no language server, so this is a self-contained Monaco definition provider that
// asks the C# host to resolve the include spec (against the project's and engine's shader roots — the page has
// no filesystem) and returns the resolved file as a definition location; the editor opener (editor.js) then
// opens it in a tab. Registered once, after editor.main loads, via window.__tbxGlsl.register(monaco).
(function () {
  "use strict";

  var bridge = window.__tbx;
  var monaco = null;
  var nextId = 1;
  var pending = Object.create(null);   // resolve-request id -> resolver

  // `#include "path"` | `#include <path>` | `#include path` (the engine accepts all three).
  var INCLUDE_RE = /^\s*#\s*include\s*(?:"([^"]+)"|<([^>]+)>|(\S+))/;

  // Host answers an includeResolve with the absolute path (or null when it couldn't be found).
  bridge.on("includeResolved", function (m) {
    var resolve = pending[m.id];
    if (!resolve) return;
    delete pending[m.id];
    resolve(m.path || null);
  });

  function resolveInclude(fromUri, spec) {
    var id = nextId++;
    return new Promise(function (resolve) {
      pending[id] = resolve;
      bridge.post({ kind: "includeResolve", id: id, from: fromUri, spec: spec });
    });
  }

  // If the cursor sits on an #include's target on this line, return the spec and its column span; else null.
  function includeAt(model, position) {
    var text = model.getLineContent(position.lineNumber);
    var m = INCLUDE_RE.exec(text);
    if (!m) return null;
    var spec = m[1] || m[2] || m[3];
    if (!spec) return null;
    var index = text.indexOf(spec);
    if (index < 0) return null;
    var startColumn = index + 1;
    var endColumn = index + spec.length + 1;
    if (position.column < startColumn || position.column > endColumn) return null;
    return { spec: spec, startColumn: startColumn, endColumn: endColumn };
  }

  window.__tbxGlsl = {
    register: function (monacoApi) {
      monaco = monacoApi;
      monaco.languages.registerDefinitionProvider("glsl", {
        provideDefinition: function (model, position) {
          var hit = includeAt(model, position);
          if (!hit) return null;
          return resolveInclude(model.uri.toString(), hit.spec).then(function (path) {
            if (!path) return null;
            return { uri: monaco.Uri.file(path.replace(/\\/g, "/")), range: new monaco.Range(1, 1, 1, 1) };
          });
        }
      });
    }
  };
})();
