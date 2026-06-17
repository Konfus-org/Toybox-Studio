using Toybox.Studio.Utils;
using System.Collections.Generic;

namespace Toybox.Studio.Services.EngineApi;

/// <summary>
/// One viewport's link to its engine view. It asks the engine to start a dedicated view (its own
/// engine camera + shared GPU texture), then surfaces that texture's cross-process handle as it
/// arrives so the owning control can import and display it directly — no pixels ever cross the
/// process boundary on the CPU. One of these is owned by each viewport/game-view instance and stops
/// its engine view on dispose, so multiple viewports stream independently.
/// </summary>
public sealed class ViewportStream : IDisposable
{
    private readonly Session _session;
    private readonly EngineRpc _engine;
    private readonly ViewKind _kind;
    private CancellationTokenSource? _cts;
    private string? _viewName;

    public ViewportStream(Session session, EngineRpc engine, ViewKind kind = ViewKind.Editor)
    {
        _session = session;
        _engine = engine;
        _kind = kind;
        session.StateChanged += OnSessionStateChanged;
        engine.SurfaceReceived += OnSurfaceReceived;

        // Opened mid-session (e.g. a new viewport while the engine is already running): start
        // right away rather than waiting for the next connect.
        if (session.State == ConnectionState.Connected)
            StartViewAsync().FireAndForget();
    }

    /// <summary>
    /// Raised (on a background thread) when this view's shared GPU texture is ready. A surface with a
    /// zero <see cref="ViewSurface.Handle"/> means GPU sharing was unavailable for this view.
    /// </summary>
    public event Action<ViewSurface>? SurfaceArrived;

    /// <summary>Raised when this view's surface is gone (disconnect or stop) so the control drops it.</summary>
    public event Action? SurfaceLost;

    public void Dispose()
    {
        // The session and engine outlive the stream, so drop subscriptions or their events would keep
        // this (disposed) instance alive.
        _session.StateChanged -= OnSessionStateChanged;
        _engine.SurfaceReceived -= OnSurfaceReceived;
        StopView();
    }

    /// <summary>
    /// Forwards the owning viewport's input to this stream's engine view (no-op until the view has
    /// started). Mouse/wheel values are deltas since the last call.
    /// </summary>
    public void SendInput(
        bool focused, int buttons, int moveKeys,
        IReadOnlyList<int> keys, double mouseX, double mouseY, double dx, double dy, double wheel)
    {
        if (_viewName is { } name && _engine.IsConnected)
            _engine.SendViewInputAsync(name, focused, buttons, moveKeys, keys, mouseX, mouseY, dx, dy, wheel)
                .FireAndForget();
    }

    private void OnSessionStateChanged(ConnectionState state)
    {
        if (state == ConnectionState.Connected)
            StartViewAsync().FireAndForget();
        else
            StopView();
    }

    private void OnSurfaceReceived(ViewSurface surface)
    {
        // The engine broadcasts every view's surface; take only this stream's.
        if (surface.Name == _viewName)
            SurfaceArrived?.Invoke(surface);
    }

    private async Task StartViewAsync()
    {
        // The engine may have no rendering service or have gone away; the viewport just stays empty.
        var result = await _engine.StartViewAsync(_kind, CancellationToken.None).ContinueOnAnyContext();
        if (result is not { Success: true, Value: { } view })
            return;

        StopView();
        _viewName = view.Name;
        _cts = new CancellationTokenSource();
        // The shared texture follows as a view.surface notification (created on the render lane).
    }

    private void StopView()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        // Free the engine-side view (camera + shared texture) for this stream. Best-effort: if the
        // connection is already gone the engine tore its views down on disconnect anyway.
        if (_viewName is { } name)
        {
            _viewName = null;
            if (_engine.IsConnected)
                _engine.StopViewAsync(name, CancellationToken.None).FireAndForget();
        }

        SurfaceLost?.Invoke();
    }
}
