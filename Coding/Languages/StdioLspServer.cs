using System.Diagnostics;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Toybox.Studio.Logging;
using Toybox.Studio.Monaco;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Coding.Languages;

/// <summary>
/// A language server spoken to over stdio with the standard <c>Content-Length</c>-framed LSP transport, bridged
/// to one <see cref="MonacoSession"/> and tagged by <see cref="Id"/> so the page routes its traffic to the right
/// client. It relays the page's LSP messages (for this tag) to the process's stdin, pumps the process's framed
/// replies/notifications back to the page tagged with <see cref="Id"/>, and tees stderr to the log. This is the
/// backend every stdio language server reuses (clangd, Roslyn); a factory supplies the launched process and the
/// languages it serves, and this owns the wiring and teardown.
/// </summary>
public sealed class StdioLspServer : ILanguageServer
{
    private readonly MonacoSession _session;
    private readonly Process _process;
    private readonly Logger _log;
    private readonly string _logTag;
    private readonly object _writeGate = new();
    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;

    /// <summary>Wires the launched <paramref name="process"/> to <paramref name="session"/> under
    /// <paramref name="id"/> and starts pumping. The caller enables the page's client (EnableLsp) after.</summary>
    public StdioLspServer(
        string id,
        IReadOnlyList<string> languageIds,
        MonacoSession session,
        Process process,
        Logger log,
        string? logTag = null)
    {
        Id = id;
        LanguageIds = languageIds;
        _session = session;
        _process = process;
        _log = log;
        _logTag = logTag ?? id;

        // Client -> server: page LSP messages for this tag forwarded to the process's stdin.
        session.LspReceived += OnLspReceived;
        // Server -> client: the process's framed output pumped back to the page; stderr tee'd to the log.
        _ = Task.Run(() => PumpServerOutputAsync(_cts.Token));
        _ = Task.Run(() => PumpServerErrorsAsync(_cts.Token));
    }

    /// <inheritdoc/>
    public string Id { get; }

    /// <inheritdoc/>
    public IReadOnlyList<string> LanguageIds { get; }

    private void OnLspReceived(string server, JObject message)
    {
        if (_disposed || server != Id)
            return;

        var json = message.ToString(Formatting.None);
        var body = Encoding.UTF8.GetBytes(json);
        var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
        try
        {
            lock (_writeGate)
            {
                // Dispose runs teardown under the same gate, so re-check inside the lock: a write that lost the
                // race to Dispose would otherwise hit a closing stdin and log spurious teardown noise.
                if (_disposed)
                    return;

                var stream = _process.StandardInput.BaseStream;
                stream.Write(header, 0, header.Length);
                stream.Write(body, 0, body.Length);
                stream.Flush();
            }
        }
        catch (Exception e)
        {
            if (!_disposed)
                _log.Warning($"{_logTag} write failed: {e.Message}");
        }
    }

    private async Task PumpServerOutputAsync(CancellationToken ct)
    {
        var stream = _process.StandardOutput.BaseStream;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var message = await ReadMessageAsync(stream, ct).ContinueOnAnyContext();
                if (message is null)
                    return; // The server exited / stream closed.

                if (ct.IsCancellationRequested || _disposed)
                    return;

                // SendLsp injects script into the WebView, which is UI-thread-affine.
                Dispatch.To(DispatchContext.UI, () =>
                {
                    if (!_disposed)
                        _session.SendLsp(Id, message);
                });
            }
        }
        catch (Exception) when (ct.IsCancellationRequested)
        {
            // Normal during teardown.
        }
        catch (Exception e)
        {
            _log.Warning($"{_logTag} read loop ended: {e.Message}");
        }
    }

    private async Task PumpServerErrorsAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await _process.StandardError.ReadLineAsync(ct).ContinueOnAnyContext();
                if (line is null)
                    return;
                if (line.Length > 0)
                    _log.Info($"[{_logTag}] {line}");
            }
        }
        catch (Exception)
        {
            // Teardown or a closed stderr stream — nothing to surface.
        }
    }

    /// <summary>Reads one <c>Content-Length</c>-framed LSP message, or null at end of stream.</summary>
    private static async Task<JObject?> ReadMessageAsync(Stream stream, CancellationToken ct)
    {
        var contentLength = await ReadContentLengthAsync(stream, ct).ContinueOnAnyContext();
        if (contentLength < 0)
            return null;

        var body = new byte[contentLength];
        var read = 0;
        while (read < contentLength)
        {
            var n = await stream.ReadAsync(body.AsMemory(read, contentLength - read), ct).ContinueOnAnyContext();
            if (n == 0)
                return null;
            read += n;
        }

        try
        {
            return JObject.Parse(Encoding.UTF8.GetString(body));
        }
        catch (JsonException)
        {
            return new JObject(); // Skip a malformed message rather than tearing the loop down.
        }
    }

    // Reads the header block byte by byte up to the blank line, returning the Content-Length (or -1 at EOF).
    private static async Task<int> ReadContentLengthAsync(Stream stream, CancellationToken ct)
    {
        var header = new StringBuilder();
        var one = new byte[1];
        var length = -1;

        while (true)
        {
            var n = await stream.ReadAsync(one.AsMemory(0, 1), ct).ContinueOnAnyContext();
            if (n == 0)
                return -1;

            header.Append((char)one[0]);
            if (one[0] != (byte)'\n')
                continue;

            var line = header.ToString();
            if (line == "\r\n" || line == "\n")
                return length; // Blank line ends the header block.

            const string marker = "Content-Length:";
            if (line.StartsWith(marker, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(line.AsSpan(marker.Length).Trim(), out var parsed))
                length = parsed;

            header.Clear();
        }
    }

    public void Dispose()
    {
        // Flip _disposed and cancel under the write gate so an in-flight write either completes before we tear
        // stdin down or sees _disposed and bails — never writes into a stream we're closing.
        lock (_writeGate)
        {
            if (_disposed)
                return;
            _disposed = true;
            _cts.Cancel();
        }

        _session.LspReceived -= OnLspReceived;
        try
        {
            if (!_process.HasExited)
                _process.Kill(entireProcessTree: true);
        }
        catch (Exception)
        {
            // The process may have already exited; nothing to clean up.
        }

        _process.Dispose();
        _cts.Dispose();
    }
}
