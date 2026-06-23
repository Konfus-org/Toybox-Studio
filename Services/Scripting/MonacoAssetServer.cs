using Toybox.Studio.Utils;
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Toybox.Studio.Services.Logging;

namespace Toybox.Studio.Services.Scripting;

/// <summary>
/// Serves the vendored Monaco bundle (copied to the output under <c>Widgets/ScriptEditor/Monaco</c>) over a loopback
/// HTTP server so the embedded WebView can load it from a real origin. We go through HTTP rather than
/// <c>file://</c> or WebView2 virtual-host mapping because Monaco spins up web workers, which need a
/// trustworthy origin — and <c>http://localhost</c> is treated as a secure context by Chromium, so workers,
/// <c>crypto.subtle</c>, etc. all work without bundling a backend-specific COM call. Loopback-only and
/// read-only (GET/HEAD), it mirrors how Studio already talks to the engine over loopback TCP. One instance is
/// shared by every editor surface (inline strip and popped-out windows alike).
/// </summary>
public sealed class MonacoAssetServer : IDisposable
{
    private readonly Logger _log;
    private readonly object _gate = new();
    private readonly string _root;

    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Uri? _baseUri;

    public MonacoAssetServer(Logger log)
    {
        _log = log;
        _root = Path.Combine(AppContext.BaseDirectory, "Widgets", "ScriptEditor", "Monaco");
    }

    /// <summary>
    /// The base URL the bundle is served from (e.g. <c>http://localhost:49231/</c>), starting the server on
    /// first use. Returns a failure if the bundle is missing or the loopback listener can't be opened.
    /// </summary>
    public Result<Uri> EnsureStarted()
    {
        lock (_gate)
        {
            if (_baseUri is not null)
                return Result<Uri>.Ok(_baseUri);

            if (!File.Exists(Path.Combine(_root, "index.html")))
                return Result<Uri>.Fail($"Monaco bundle not found at '{_root}'. Was it copied to the output?");

            int port;
            try
            {
                port = FreeLoopbackPort();
            }
            catch (Exception e)
            {
                return Result<Uri>.Fail($"Couldn't reserve a loopback port: {e.Message}");
            }

            var prefix = $"http://localhost:{port}/";
            var listener = new HttpListener();
            listener.Prefixes.Add(prefix);
            try
            {
                listener.Start();
            }
            catch (Exception e)
            {
                return Result<Uri>.Fail($"Couldn't start the editor asset server on {prefix}: {e.Message}");
            }

            _listener = listener;
            _cts = new CancellationTokenSource();
            _baseUri = new Uri(prefix);
            _ = Task.Run(() => ServeAsync(listener, _cts.Token));
            _log.Info($"Script editor assets served at {prefix}");
            return Result<Uri>.Ok(_baseUri);
        }
    }

    private async Task ServeAsync(HttpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync().ContinueOnAnyContext();
            }
            catch (Exception) when (ct.IsCancellationRequested)
            {
                return; // Listener stopped during teardown.
            }
            catch (Exception)
            {
                return; // Listener faulted (e.g. disposed); nothing left to serve.
            }

            try
            {
                Respond(context);
            }
            catch (Exception e)
            {
                _log.Warning($"Editor asset request failed: {e.Message}");
                TrySetStatus(context, HttpStatusCode.InternalServerError);
            }
        }
    }

    private void Respond(HttpListenerContext context)
    {
        var response = context.Response;
        var method = context.Request.HttpMethod;
        if (method != "GET" && method != "HEAD")
        {
            response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
            response.Close();
            return;
        }

        var file = ResolveFile(context.Request.Url);
        if (file is null || !File.Exists(file))
        {
            response.StatusCode = (int)HttpStatusCode.NotFound;
            response.Close();
            return;
        }

        var bytes = File.ReadAllBytes(file);
        response.ContentType = ContentTypeFor(file);
        response.ContentLength64 = bytes.Length;
        // The bundle is versioned with the app build, so let the WebView cache it between launches.
        response.Headers["Cache-Control"] = "public, max-age=86400";
        if (method == "GET")
            response.OutputStream.Write(bytes, 0, bytes.Length);
        response.Close();
    }

    /// <summary>
    /// Maps a request URL to a file under the bundle root, refusing anything that escapes it (path traversal).
    /// </summary>
    private string? ResolveFile(Uri? url)
    {
        var relative = Uri.UnescapeDataString(url?.AbsolutePath ?? "/").TrimStart('/');
        if (relative.Length == 0)
            relative = "index.html";

        var combined = Path.GetFullPath(Path.Combine(_root, relative));
        var rootFull = Path.GetFullPath(_root) + Path.DirectorySeparatorChar;
        return combined.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase) ? combined : null;
    }

    private static string ContentTypeFor(string file) => Path.GetExtension(file).ToLowerInvariant() switch
    {
        ".html" => "text/html; charset=utf-8",
        ".js" => "text/javascript; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".json" => "application/json; charset=utf-8",
        ".map" => "application/json; charset=utf-8",
        ".ttf" => "font/ttf",
        ".woff" => "font/woff",
        ".woff2" => "font/woff2",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        _ => "application/octet-stream",
    };

    private static int FreeLoopbackPort()
    {
        // Bind to port 0 to let the OS pick a free loopback port, then hand it to HttpListener. The brief gap
        // between closing this probe and HttpListener binding is a negligible race for a single-user editor.
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        try
        {
            return ((IPEndPoint)probe.LocalEndpoint).Port;
        }
        finally
        {
            probe.Stop();
        }
    }

    private static void TrySetStatus(HttpListenerContext context, HttpStatusCode code)
    {
        try
        {
            context.Response.StatusCode = (int)code;
            context.Response.Close();
        }
        catch (Exception)
        {
            // The response may already be (partly) sent; nothing more we can do.
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _cts?.Cancel();
            _listener?.Close();
            _listener = null;
            _cts?.Dispose();
            _cts = null;
            _baseUri = null;
        }
    }
}
