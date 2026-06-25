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
    private readonly long _assetId;

    // Serializes start/stop so overlapping StartViewAsync calls (e.g. a reconnect racing the mid-session
    // open) can't both run StartView and leak an engine view, and so _viewName/_cts are never written
    // concurrently. A start always stops the previous view (and awaits its view.stop) before switching.
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _cts;
    private volatile string? _viewName;
    private bool _disposed;

    public ViewportStream(
        Session session, EngineRpc engine, ViewKind kind = ViewKind.Editor, long assetId = 0)
    {
        _session = session;
        _engine = engine;
        _kind = kind;
        _assetId = assetId;
        session.StateChanged += OnSessionStateChanged;
        engine.SurfaceReceived += OnSurfaceReceived;
        engine.MouseLockModeChanged += OnMouseLockModeChanged;
        engine.BillboardsReceived += OnBillboardsReceived;

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

    /// <summary>
    /// Raised (on the RPC listener thread) with this view's per-frame projected entity positions for the
    /// billboard overlay (name labels + component icon stacks). Only this stream's view is forwarded.
    /// </summary>
    public event Action<IReadOnlyList<BillboardPosition>>? BillboardsArrived;

    /// <summary>
    /// Raised when the playing game's mouse-lock mode changes ("unlocked", "relative", or "grabbed"),
    /// forwarded from the engine. Lets the game viewport capture the cursor for mouselook without holding the
    /// engine transport itself.
    /// </summary>
    public event Action<string>? MouseLockModeChanged;

    public void Dispose()
    {
        // The session and engine outlive the stream, so drop subscriptions or their events would keep
        // this (disposed) instance alive.
        _session.StateChanged -= OnSessionStateChanged;
        _engine.SurfaceReceived -= OnSurfaceReceived;
        _engine.MouseLockModeChanged -= OnMouseLockModeChanged;
        _engine.BillboardsReceived -= OnBillboardsReceived;
        _disposed = true;
        StopViewAsync().FireAndForget();
    }

    /// <summary>
    /// Forwards the owning viewport's input to this stream's engine view (no-op until the view has
    /// started). Mouse/wheel values are deltas since the last call.
    /// </summary>
    public void SendInput(
        bool focused, int buttons, int moveKeys,
        IReadOnlyList<InputKey> keys, double mouseX, double mouseY, double dx, double dy, double wheel,
        double cursorU, double cursorV)
    {
        if (_viewName is { } name && _engine.IsConnected)
            _engine.SendViewInputAsync(
                    name, focused, buttons, moveKeys, keys, mouseX, mouseY, dx, dy, wheel, cursorU, cursorV)
                .FireAndForget();
    }

    /// <summary>
    /// Rebuilds this asset-preview view with a different mesh/material option (no-op until the view has
    /// started or for non-preview views). Fire-and-forget.
    /// </summary>
    public void SetPreviewOption(string option)
    {
        if (_viewName is { } name && _engine.IsConnected)
            _engine.SetPreviewOptionAsync(name, option).FireAndForget();
    }

    /// <summary>Changes this asset-preview view's background sky (no-op until the view has started).</summary>
    public void SetPreviewSkybox(string skybox)
    {
        if (_viewName is { } name && _engine.IsConnected)
            _engine.SetPreviewSkyboxAsync(name, skybox).FireAndForget();
    }

    /// <summary>
    /// Picks the entity under a normalized viewport coordinate (top-left origin) on this stream's engine view.
    /// Fails (without changing selection) when the view hasn't started or the engine is gone.
    /// </summary>
    public Task<Result<ulong?>> PickAsync(double u, double v) =>
        _viewName is { } name && _engine.IsConnected
            ? _engine.PickAsync(name, u, v, CancellationToken.None)
            : Task.FromResult(Result<ulong?>.Fail("The view has not started."));

    /// <summary>
    /// Box-selects entities inside a normalized marquee rect (top-left origin) on this stream's view.
    /// Fails (selection unchanged) when the view hasn't started or the engine is gone.
    /// </summary>
    public Task<Result<IReadOnlyList<ulong>>> PickRectAsync(double u0, double v0, double u1, double v1) =>
        _viewName is { } name && _engine.IsConnected
            ? _engine.PickRectAsync(name, u0, v0, u1, v1, CancellationToken.None)
            : Task.FromResult(Result<IReadOnlyList<ulong>>.Fail("The view has not started."));

    /// <summary>
    /// Which of the given entities are occluded from this stream's view (billboard overlay visibility).
    /// Fails when the view hasn't started or the engine is gone, leaving icons at their last visibility.
    /// </summary>
    public Task<Result<IReadOnlyList<bool>>> QueryOcclusionAsync(IReadOnlyList<ulong> ids) =>
        _viewName is { } name && _engine.IsConnected
            ? _engine.QueryOcclusionAsync(name, ids, CancellationToken.None)
            : Task.FromResult(Result<IReadOnlyList<bool>>.Fail("The view has not started."));

    private void OnMouseLockModeChanged(string mode) => MouseLockModeChanged?.Invoke(mode);

    private void OnBillboardsReceived(string view, IReadOnlyList<BillboardPosition> positions)
    {
        // The engine sends one message per editor view; take only this stream's.
        if (view == _viewName)
            BillboardsArrived?.Invoke(positions);
    }

    private void OnSessionStateChanged(ConnectionState state)
    {
        if (state == ConnectionState.Connected)
            StartViewAsync().FireAndForget();
        else
            StopViewAsync().FireAndForget();
    }

    private void OnSurfaceReceived(ViewSurface surface)
    {
        // The engine broadcasts every view's surface; take only this stream's.
        if (surface.Name == _viewName)
            SurfaceArrived?.Invoke(surface);
    }

    private async Task StartViewAsync()
    {
        await _gate.WaitAsync().ContinueOnAnyContext();
        try
        {
            if (_disposed)
                return;

            // Stop (and await) any previous view first, so a re-entrant start can't leak the old engine
            // view/camera before this stream switches to the new one.
            await StopViewLockedAsync().ContinueOnAnyContext();

            // The engine may have no rendering service or have gone away; the viewport just stays empty.
            var result = await _engine.StartViewAsync(_kind, CancellationToken.None, _assetId).ContinueOnAnyContext();
            if (_disposed || result is not { Success: true, Value: { } view })
            {
                // Disposed (or torn down) while the call was in flight: don't keep an orphaned engine view.
                if (result is { Success: true, Value: { } orphan } && _engine.IsConnected)
                    await _engine.StopViewAsync(orphan.Name, CancellationToken.None).ContinueOnAnyContext();
                return;
            }

            _viewName = view.Name;
            _cts = new CancellationTokenSource();
            // The shared texture follows as a view.surface notification (created on the render lane).
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task StopViewAsync()
    {
        await _gate.WaitAsync().ContinueOnAnyContext();
        try
        {
            await StopViewLockedAsync().ContinueOnAnyContext();
        }
        finally
        {
            _gate.Release();
        }
    }

    // Tears the current view down; the caller must hold _gate.
    private async Task StopViewLockedAsync()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        // Free the engine-side view (camera + shared texture) for this stream. Awaited (not fire-and-forget)
        // so the old view.stop is actually sent before a following StartView, preventing a leaked view.
        // Best-effort: if the connection is already gone the engine tore its views down on disconnect anyway.
        if (_viewName is { } name)
        {
            _viewName = null;
            if (_engine.IsConnected)
                await _engine.StopViewAsync(name, CancellationToken.None).ContinueOnAnyContext();
        }

        SurfaceLost?.Invoke();
    }
}
