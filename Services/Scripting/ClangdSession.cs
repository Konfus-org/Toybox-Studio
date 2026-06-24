using Toybox.Studio.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Toybox.Studio.Services.Logging;

namespace Toybox.Studio.Services.Scripting;

/// <summary>
/// Bridges one <see cref="MonacoSession"/> to a clangd process: the page's LSP client talks JSON-RPC, this
/// relays each message to clangd over stdio (the standard <c>Content-Length</c>-framed transport) and pumps
/// clangd's replies and diagnostics back. clangd is pointed at the project's <c>compile_commands.json</c> with
/// background indexing on, so completion / go-to-definition / diagnostics span the engine headers and every
/// sibling script — clangd sees one project graph, not the edited file alone.
/// </summary>
public sealed class ClangdSession : IDisposable
{
    private readonly MonacoSession _session;
    private readonly Process _process;
    private readonly Logger _log;
    private readonly object _writeGate = new();
    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;

    private ClangdSession(MonacoSession session, Process process, Logger log)
    {
        _session = session;
        _process = process;
        _log = log;
    }

    /// <summary>
    /// Spawns clangd for <paramref name="projectRoot"/> and wires it to <paramref name="session"/>. Fails (the
    /// editor falls back to highlight-only) if clangd can't be found or launched. <paramref name="engineRoot"/>
    /// (when known) seeds the project's <c>.clangd</c> so script files — which the project build doesn't export
    /// into <c>compile_commands.json</c> — still resolve the engine headers.
    /// </summary>
    public static Result<ClangdSession> Start(
        MonacoSession session, string projectRoot, string? engineRoot, Logger log)
    {
        var located = ClangdLocator.Locate();
        if (!located)
            return Result<ClangdSession>.Fail(located.Error!);

        EnsureConfig(projectRoot, engineRoot, log);

        var info = new ProcessStartInfo
        {
            FileName = located.Value!,
            WorkingDirectory = projectRoot,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        info.ArgumentList.Add("--background-index");
        info.ArgumentList.Add("--pch-storage=memory");
        info.ArgumentList.Add("--limit-results=100");
        info.ArgumentList.Add("--log=error");
        if (CompileCommandsDir(projectRoot) is { } dir)
            info.ArgumentList.Add($"--compile-commands-dir={dir}");

        Process process;
        try
        {
            process = Process.Start(info) ?? throw new InvalidOperationException("Process.Start returned null.");
        }
        catch (Exception e)
        {
            return Result<ClangdSession>.Fail($"Couldn't start clangd: {e.Message}");
        }

        var clangd = new ClangdSession(session, process, log);
        // Client -> server: page LSP messages forwarded to clangd's stdin.
        session.LspReceived += clangd.SendToServer;
        // Server -> client: clangd's framed output pumped back to the page; stderr tee'd to the log.
        _ = Task.Run(() => clangd.PumpServerOutputAsync(clangd._cts.Token));
        _ = Task.Run(() => clangd.PumpServerErrorsAsync(clangd._cts.Token));
        return Result<ClangdSession>.Ok(clangd);
    }

    private void SendToServer(JObject message)
    {
        if (_disposed)
            return;

        var json = message.ToString(Formatting.None);
        var body = Encoding.UTF8.GetBytes(json);
        var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
        try
        {
            lock (_writeGate)
            {
                // Dispose runs its teardown under the same gate, so re-check inside the lock: a write that lost
                // the race to Dispose would otherwise hit a closing stdin and log spurious teardown noise.
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
            // Swallow quietly once teardown began — a closing stdin throwing is expected, not worth surfacing.
            if (!_disposed)
                _log.Warning($"clangd write failed: {e.Message}");
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
                    return; // clangd exited / stream closed.

                // Drop late messages once teardown began: Dispose cancels _cts and the WebView may already be
                // detached, so dispatching now would hit a dead surface. Re-check inside the lambda too, since
                // the UI post is async and Dispose can win the race after we queue it.
                if (ct.IsCancellationRequested || _disposed)
                    return;

                // SendLsp injects script into the WebView, which is UI-thread-affine.
                Dispatch.To(DispatchContext.UI, () =>
                {
                    if (!_disposed)
                        _session.SendLsp(message);
                });
            }
        }
        catch (Exception) when (ct.IsCancellationRequested)
        {
            // Normal during teardown.
        }
        catch (Exception e)
        {
            _log.Warning($"clangd read loop ended: {e.Message}");
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
                // clangd runs with --log=error, so anything on stderr is worth surfacing.
                if (line.Length > 0)
                    _log.Info($"[clangd] {line}");
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

    // Marks our generated .clangd so we can upgrade it when the template changes without clobbering a config
    // the user wrote themselves. Bump when the flag set below changes.
    private const string ConfigMarker = "# Generated by Toybox Studio for the script editor";

    // Writes a project .clangd so script translation units — which the project build doesn't export into
    // compile_commands.json — still resolve their headers: the engine source roots, the engine's and the
    // script's GENERATED headers (each script .cpp first-includes its <name>.generated.h, and engine headers
    // pull in tbx/.../<x>.generated.h), plus -Wno-unknown-attributes and a generated/inl diagnostics ignore.
    // Only (re)written when absent or when the existing file is our own earlier generated one.
    private static void EnsureConfig(string projectRoot, string? engineRoot, Logger log)
    {
        var path = Path.Combine(projectRoot, ".clangd");
        if (File.Exists(path) && !IsStudioGenerated(path))
            return; // The user wrote their own — leave it.

        var add = new List<string> { "-std=c++23", "-Wno-unknown-attributes" };
        if (engineRoot is not null)
        {
            var root = engineRoot.Replace('\\', '/').TrimEnd('/');
            add.Add($"-I{root}/engine/include");
            add.Add($"-I{root}/plugins/cpp_scripting/runtime/include");
        }

        foreach (var dir in GeneratedIncludeDirs(Path.Combine(projectRoot, "build")))
            add.Add($"-I{dir.Replace('\\', '/')}");

        var lines = new List<string>
        {
            $"{ConfigMarker}. Safe to edit or delete.",
            "CompileFlags:",
            "  Add:",
        };
        lines.AddRange(add.Select(flag => $"    - {flag}"));
        lines.Add("Diagnostics:");
        lines.Add("  Includes:");
        lines.Add("    IgnoreHeader:");
        lines.Add("      - '.*\\.generated\\.h'");
        lines.Add("      - '.*\\.inl'");

        try
        {
            File.WriteAllText(path, string.Join("\n", lines) + "\n");
            log.Info($"Script editor: wrote {path} for clangd include resolution.");
        }
        catch (Exception e)
        {
            log.Warning($"Script editor: couldn't write .clangd: {e.Message}");
        }
    }

    private static bool IsStudioGenerated(string path)
    {
        try
        {
            using var reader = new StreamReader(path);
            return reader.ReadLine()?.StartsWith(ConfigMarker, StringComparison.Ordinal) == true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // The codegen output directories under the build tree that hold the generated headers script and engine
    // sources include. Probed at known project-layout locations (the script's <name>.generated.h sits in a
    // generated/scripts folder; the engine's tbx/.../<x>.generated.h under engine's generated/include).
    private static IEnumerable<string> GeneratedIncludeDirs(string buildDir)
    {
        string[] candidates =
        [
            Path.Combine(buildDir, "engine", "engine", "generated", "include"),
            Path.Combine(buildDir, "generated", "scripts"),
            Path.Combine(buildDir, "Scripts", "generated", "scripts"),
            Path.Combine(buildDir, "Source", "Scripts", "generated", "scripts"),
        ];

        foreach (var dir in candidates)
            if (Directory.Exists(dir))
                yield return dir;
    }

    // clangd reads compile_commands.json from this directory; prefer the project's build/ folder, then root.
    private static string? CompileCommandsDir(string projectRoot)
    {
        foreach (var dir in new[] { Path.Combine(projectRoot, "build"), projectRoot })
        {
            if (File.Exists(Path.Combine(dir, "compile_commands.json")))
                return dir;
        }

        return null;
    }

    public void Dispose()
    {
        // Flip _disposed and cancel under the write gate so an in-flight SendToServer either completes before
        // we tear stdin down or sees _disposed and bails — never writes into a stream we're closing.
        lock (_writeGate)
        {
            if (_disposed)
                return;
            _disposed = true;
            _cts.Cancel();
        }

        _session.LspReceived -= SendToServer;
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
