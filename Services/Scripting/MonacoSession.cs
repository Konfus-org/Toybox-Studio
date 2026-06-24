using Toybox.Studio.Utils;
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Toybox.Studio.Services.Scripting;

/// <summary>
/// The C# end of the bridge to one Monaco WebView. It owns the message protocol — a JSON envelope keyed by
/// <c>kind</c> — independent of which WebView control hosts it: a control calls <see cref="AttachTransport"/>
/// to supply the outbound channel (host → page) and forwards inbound page messages to <see cref="Receive"/>.
/// Outbound commands sent before the page reports <c>ready</c> are queued and flushed on ready, so callers
/// can <see cref="OpenDocument"/> immediately after creating the view. Inbound page events surface as plain
/// C# events that a view-model subscribes to. One session backs one WebView; the shared document model
/// (<see cref="ScriptDocument"/>) is what keeps two sessions showing the same file in sync.
/// </summary>
public sealed class MonacoSession
{
    private readonly Uri _pageUri;
    private readonly Queue<string> _pending = new();
    private readonly JsonSerializerSettings _json = new() { NullValueHandling = NullValueHandling.Ignore };

    private Action<string>? _send;
    private bool _ready;

    public MonacoSession(Uri pageUri)
    {
        _pageUri = pageUri;
    }

    /// <summary>The page URL the hosting WebView should navigate to.</summary>
    public Uri PageUri => _pageUri;

    /// <summary>Raised once the page has loaded and is ready to receive commands.</summary>
    public event Action? Ready;

    /// <summary>The active document's text changed in the editor (path, new text, Monaco version id).</summary>
    public event Action<string, string, int>? ContentChanged;

    /// <summary>The cursor moved (1-based line, column) — drives the status bar.</summary>
    public event Action<int, int>? CursorMoved;

    /// <summary>The page's language client reported a state ("ready" | "error") — drives the status bar.</summary>
    public event Action<string>? LspStatusChanged;

    /// <summary>The user asked to save the given document (Ctrl+S) with its current text.</summary>
    public event Action<string, string>? SaveRequested;

    /// <summary>An LSP message arrived from the page's language client (Phase 4).</summary>
    public event Action<JObject>? LspReceived;

    public bool IsReady => _ready;

    /// <summary>Wires the outbound channel (a control's script-injection call). Flushes any queued commands.</summary>
    public void AttachTransport(Action<string> send)
    {
        _send = send;
        if (_ready)
            Flush();
    }

    /// <summary>Drops the outbound channel (the control detached). The page may be navigated away.</summary>
    public void DetachTransport()
    {
        _send = null;
        _ready = false;
        // Detaching navigates the page away, so the freshly-loaded page on re-attach has no models. Any
        // commands still queued (e.g. opens for documents closed while detached) would replay blindly against
        // that blank page; drop them. Live surfaces re-issue their open/setActive on re-attach.
        _pending.Clear();
    }

    /// <summary>Opens (or replaces the content of) a document and makes it the active editor model.</summary>
    public void OpenDocument(string path, string text, string? language = null) =>
        Post(new { kind = "open", path, text, language });

    /// <summary>Switches the visible model to an already-open document, preserving its undo/scroll state.</summary>
    public void SetActive(string path) => Post(new { kind = "setActive", path });

    /// <summary>Disposes a document's model in the page.</summary>
    public void CloseDocument(string path) => Post(new { kind = "close", path });

    /// <summary>Pushes editor options (minimap, font size, read-only, line numbers, word wrap).</summary>
    public void SetOptions(
        bool? minimap = null, int? fontSize = null, bool? readOnly = null,
        string? lineNumbers = null, string? wordWrap = null) =>
        Post(new { kind = "options", minimap, fontSize, readOnly, lineNumbers, wordWrap });

    /// <summary>Switches the Monaco colour theme to match the editor's light/dark base.</summary>
    public void SetTheme(bool dark) => Post(new { kind = "theme", @base = dark ? "dark" : "light" });

    /// <summary>Forwards an LSP message to the page's language client.</summary>
    public void SendLsp(JObject message) => Post(new { kind = "lsp", message });

    /// <summary>
    /// Turns the page's LSP client on once clangd is running, handing it the workspace root URI for the
    /// <c>initialize</c> handshake. Until this is sent the client stays dormant (so surfaces without a clangd
    /// behind them — the inline strip — don't try to start a language session).
    /// </summary>
    public void EnableLsp(string rootUri) => Post(new { kind = "lspEnable", rootUri });

    /// <summary>Handles one inbound JSON envelope from the page (called on the UI thread by the control).</summary>
    public void Receive(string body)
    {
        JObject envelope;
        try
        {
            var token = JToken.Parse(body);
            // Depending on the WebView backend the payload can arrive as the JSON object directly or as a
            // JSON-encoded string of it; unwrap the latter so either form parses to the envelope.
            envelope = token as JObject
                       ?? (token.Type == JTokenType.String ? JObject.Parse((string)token!) : null!);
            if (envelope is null)
                return;
        }
        catch (JsonException)
        {
            return; // A malformed page message is dropped rather than crashing the host.
        }

        switch ((string?)envelope["kind"])
        {
            case "ready":
                _ready = true;
                Flush();
                Ready?.Invoke();
                break;

            case "editor":
                DispatchEditor(envelope);
                break;

            case "fs" when (string?)envelope["type"] == "save":
                SaveRequested?.Invoke((string?)envelope["path"] ?? "", (string?)envelope["text"] ?? "");
                break;

            case "lsp" when envelope["message"] is JObject message:
                LspReceived?.Invoke(message);
                break;
        }
    }

    private void DispatchEditor(JObject envelope)
    {
        switch ((string?)envelope["type"])
        {
            case "change":
                ContentChanged?.Invoke(
                    (string?)envelope["path"] ?? "",
                    (string?)envelope["text"] ?? "",
                    (int?)envelope["version"] ?? 0);
                break;

            case "cursor":
                CursorMoved?.Invoke((int?)envelope["line"] ?? 1, (int?)envelope["column"] ?? 1);
                break;

            case "lspStatus":
                LspStatusChanged?.Invoke((string?)envelope["state"] ?? "");
                break;
        }
    }

    private void Post(object envelope)
    {
        var json = JsonConvert.SerializeObject(envelope, _json);
        if (_ready && _send is not null)
            _send(json);
        else
            _pending.Enqueue(json);
    }

    private void Flush()
    {
        if (_send is null)
            return;

        while (_pending.Count > 0)
            _send(_pending.Dequeue());
    }
}
