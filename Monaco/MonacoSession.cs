using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Toybox.Studio.Monaco;

/// <summary>
/// The C# end of the bridge to one Monaco WebView. It owns the message protocol — a JSON envelope keyed by
/// <c>kind</c> — independent of which WebView control hosts it: a control calls <see cref="AttachTransport"/>
/// to supply the outbound channel (host → page) and forwards inbound page messages to <see cref="Receive"/>.
/// Outbound commands sent before the page reports <c>ready</c> are queued and flushed on ready, so callers
/// can <see cref="OpenDocument"/> immediately after creating the view. Inbound page events surface as plain
/// C# events that a view-model subscribes to. One session backs one WebView; the shared document model
/// (<c>ScriptDocument</c>) is what keeps two sessions showing the same file in sync.
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

    /// <summary>A language client reported a state ("starting" | "ready" | "error"), tagged with its server id
    /// — drives the status bar.</summary>
    public event Action<string, string>? LspStatusChanged;

    /// <summary>The user asked to save the given document (Ctrl+S) with its current text.</summary>
    public event Action<string, string>? SaveRequested;

    /// <summary>An LSP message arrived from a page language client, tagged with the server id it belongs to.</summary>
    public event Action<string, JObject>? LspReceived;

    /// <summary>The page asked to resolve a GLSL <c>#include</c>: (request id, including file uri, include spec).
    /// The handler answers with <see cref="ResolveIncludeResult"/>.</summary>
    public event Action<int, string, string>? IncludeResolveRequested;

    /// <summary>The page asked the host to open a file in a tab (a cross-file go-to-definition target): (file
    /// uri, 1-based line or 0). The host opens it in the editor.</summary>
    public event Action<string, int>? OpenFileRequested;

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

    /// <summary>Makes the document active (if needed) and scrolls it to <paramref name="line"/> (1-based),
    /// putting the caret at the start of that line and focusing the editor.</summary>
    public void RevealLine(string path, int line) => Post(new { kind = "reveal", path, line });

    /// <summary>Disposes a document's model in the page.</summary>
    public void CloseDocument(string path) => Post(new { kind = "close", path });

    /// <summary>Pushes editor options (minimap, font size, read-only, line numbers, word wrap).</summary>
    public void SetOptions(
        bool? minimap = null, int? fontSize = null, bool? readOnly = null,
        string? lineNumbers = null, string? wordWrap = null) =>
        Post(new { kind = "options", minimap, fontSize, readOnly, lineNumbers, wordWrap });

    /// <summary>
    /// Switches the Monaco colour theme to match the editor's light/dark base, and (when given) recolours the
    /// editor's accents — cursor, selection — to <paramref name="primaryHex"/> (an <c>"RRGGBB"</c> string, no
    /// leading <c>#</c>) so the editor picks up the app theme's primary colour. Null leaves the accents at the
    /// theme's built-in defaults.
    /// </summary>
    public void SetTheme(bool dark, string? primaryHex = null) =>
        Post(new { kind = "theme", @base = dark ? "dark" : "light", primary = primaryHex });

    /// <summary>Forwards an LSP message to the page language client tagged <paramref name="server"/>.</summary>
    public void SendLsp(string server, JObject message) => Post(new { kind = "lsp", server, message });

    /// <summary>
    /// Turns on the page's LSP client for the server tagged <paramref name="server"/> once its process is
    /// running, handing it the workspace root URI for the <c>initialize</c> handshake and the Monaco language
    /// ids the server serves (so the page registers completion/hover/etc. providers for exactly those languages).
    /// <paramref name="initializationOptions"/> rides along in <c>initialize</c>; <paramref name="postInitialize"/>
    /// is a list of <c>{ method, params }</c> notifications the client sends right after <c>initialized</c> (e.g.
    /// Roslyn's <c>solution/open</c>). Until this is sent the client stays dormant, so surfaces with no server
    /// behind a language never start a session for it.
    /// </summary>
    public void EnableLsp(
        string server,
        string rootUri,
        IReadOnlyList<string> languages,
        object? initializationOptions = null,
        IReadOnlyList<object>? postInitialize = null) =>
        Post(new { kind = "lspEnable", server, rootUri, languages, initializationOptions, postInitialize });

    /// <summary>Answers a page <c>includeResolve</c> request with the resolved absolute path (null if unresolved).</summary>
    public void ResolveIncludeResult(int id, string? path) => Post(new { kind = "includeResolved", id, path });

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
                SaveRequested?.Invoke((string?)envelope["path"] ?? string.Empty, (string?)envelope["text"] ?? string.Empty);
                break;

            case "lsp" when envelope["message"] is JObject message:
                LspReceived?.Invoke((string?)envelope["server"] ?? string.Empty, message);
                break;

            case "includeResolve":
                IncludeResolveRequested?.Invoke(
                    (int?)envelope["id"] ?? 0,
                    (string?)envelope["from"] ?? string.Empty,
                    (string?)envelope["spec"] ?? string.Empty);
                break;

            case "openFile":
                OpenFileRequested?.Invoke((string?)envelope["uri"] ?? string.Empty, (int?)envelope["line"] ?? 0);
                break;
        }
    }

    private void DispatchEditor(JObject envelope)
    {
        switch ((string?)envelope["type"])
        {
            case "change":
                ContentChanged?.Invoke(
                    (string?)envelope["path"] ?? string.Empty,
                    (string?)envelope["text"] ?? string.Empty,
                    (int?)envelope["version"] ?? 0);
                break;

            case "cursor":
                CursorMoved?.Invoke((int?)envelope["line"] ?? 1, (int?)envelope["column"] ?? 1);
                break;

            case "lspStatus":
                LspStatusChanged?.Invoke(
                    (string?)envelope["server"] ?? string.Empty,
                    (string?)envelope["state"] ?? string.Empty);
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
