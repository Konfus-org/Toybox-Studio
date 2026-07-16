// Transport seam between the Monaco web layer and the C# host.
//
// Every message is a JSON envelope with a `kind` discriminator ("ready" | "editor" | "fs" | "lsp" | ...).
// Outbound (web -> host) goes over WebView2's postMessage channel when present, falling back to the
// Avalonia NativeWebView `invokeCSharpAction` shim. Inbound (host -> web) is delivered by the host calling
// `window.__tbx.receive(json)` via script injection; handlers register by `kind`.
(function () {
  "use strict";

  var handlers = Object.create(null);

  function post(envelope) {
    var json = JSON.stringify(envelope);
    // Prefer Avalonia's injected shim — it surfaces the string verbatim as WebMessageReceivedEventArgs.Body.
    // Fall back to WebView2's raw channel where the shim isn't present.
    if (typeof window.invokeCSharpAction === "function") {
      window.invokeCSharpAction(json);
    } else if (window.chrome && window.chrome.webview && window.chrome.webview.postMessage) {
      window.chrome.webview.postMessage(json);
    } else {
      console.warn("[tbx] no host bridge available; dropped", envelope.kind);
    }
  }

  function on(kind, handler) {
    handlers[kind] = handler;
  }

  // Called by the host (CoreWebView2.ExecuteScriptAsync / NativeWebView.InvokeScript).
  function receive(json) {
    var envelope;
    try {
      envelope = typeof json === "string" ? JSON.parse(json) : json;
    } catch (e) {
      console.error("[tbx] bad inbound envelope", e);
      return;
    }
    var handler = handlers[envelope.kind];
    if (handler) {
      handler(envelope);
    } else {
      console.warn("[tbx] no handler for kind", envelope.kind);
    }
  }

  window.__tbx = { post: post, on: on, receive: receive };
})();
