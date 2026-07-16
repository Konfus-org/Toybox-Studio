using Toybox.Studio.Events;
using Toybox.Studio.Hosting;
using Toybox.Studio.Input;
using Toybox.Studio.Utils;

namespace Toybox.Studio.EngineApi;

/// <summary>
/// Which engine camera a viewport stream renders. <see cref="Editor"/> is a free camera spawned at
/// the game camera's position; <see cref="Game"/> mirrors the actual game camera every frame;
/// <see cref="AssetPreview"/> orbits an isolated world holding a single previewed asset.
/// </summary>
public enum ViewKind
{
    Editor,
    Game,
    AssetPreview,
}

/// <summary>The engine's reply to view.start: the new view's id, and the world it renders (0 for
/// editor/game views bound to the active world; for an asset-preview view the engine echoes back the
/// preview world id the editor provisioned and passed in).</summary>
public sealed record ViewInfo(string Name, uint WorldAssetId = 0);

/// <summary>
/// One viewport's link to its engine view. It asks the engine to start a dedicated view (its own
/// engine camera + shared GPU texture), then surfaces that texture's cross-process handle as it
/// arrives so the owning control can import and display it directly — no pixels ever cross the
/// process boundary on the CPU. One of these is owned by each viewport/game-view instance and stops
/// its engine view on dispose, so multiple viewports stream independently.
///
/// This is the viewport concern's owner of the <c>view.*</c> RPC vocabulary: it builds each command and
/// parses each reply against the engine's generic command entry points, so the engine facade stays free
/// of viewport-specific methods. The owning control watches the globally dispatched
/// <see cref="ViewSurfaceCreated"/> events, filtering by <see cref="ViewName"/>.
/// </summary>
public sealed class ViewportStream :
    EventSubscriber,
    IEventHandler<ConnectionChanged>,
    IEventHandler<ViewSurfaceCreated>
{
    private readonly Engine _engine;
    private readonly ViewKind _kind;

    // Asset-preview only: the owner-supplied hooks that provision (load + populate) the preview world this
    // view renders and release it once the view stops, whether the camera auto-orbits (a turntable), and a
    // render-resolution scale (0..1] for a cheaper small preview. The bridge owns no preview content — the
    // editor loads the preview world and its assets through these hooks and binds the view to it.
    private readonly Func<Task<Result<uint>>>? _provisionWorld;
    private readonly Func<uint, Task>? _releaseWorld;
    private readonly bool _turntable;
    private readonly double _renderScale;

    // Serializes start/stop so overlapping StartViewAsync calls (e.g. a reconnect racing the mid-session
    // open) can't both run StartView and leak an engine view, and so _viewName is never written
    // concurrently. A start always stops the previous view (and awaits its view.stop) before switching.
    private readonly SemaphoreSlim _gate = new(1, 1);

    private volatile string? _viewName;
    private bool _disposed;

    // Editor views only: the viewport's on-screen size in device pixels, sent with view.start so the engine
    // renders the pane at its own resolution rather than the full graphics resolution. The first view is
    // started once, deferred until the pane has been laid out (see TryStartEditorView / _startRequested).
    // A later resize recreates the view at the new size — but in the BACKGROUND: it starts the replacement
    // view (the old one keeps rendering, so its shared texture's keyed mutex stays satisfied and the
    // compositor never blocks), swaps the display to the replacement once its first frame is ready, and
    // only then stops the old view. The resize is debounced so a drag recreates once, on release, not every
    // frame. _appliedLong is the long edge the live view was started with (skips no-op recreates).
    private int _paneWidth;
    private int _paneHeight;
    private bool _startRequested;
    private int _appliedLong;
    private CancellationTokenSource? _resizeDebounce;
    // While a background recreate waits for its replacement view's first surface: the replacement's name
    // and a completion source the ViewSurfaceCreated handler trips when that surface is announced.
    private string? _awaitingSurfaceFor;
    private TaskCompletionSource<bool>? _surfaceReady;

    public ViewportStream(
        Engine engine,
        EventDispatcher events,
        ViewKind kind = ViewKind.Editor,
        Func<Task<Result<uint>>>? provisionWorld = null,
        Func<uint, Task>? releaseWorld = null,
        bool turntable = false,
        double renderScale = 1.0)
        : base(events)
    {
        _engine = engine;
        _kind = kind;
        _provisionWorld = provisionWorld;
        _releaseWorld = releaseWorld;
        _turntable = turntable;
        _renderScale = renderScale;

        // Opened mid-session (e.g. a new viewport while the engine is already running): start right away
        // rather than waiting for the next connect. An editor view instead waits until it knows its pane
        // size (see SetRenderSize) so it opens at the pane's resolution; game/preview views size to their
        // own content and start immediately.
        if (engine.IsConnected)
        {
            if (_kind == ViewKind.Editor)
                TryStartEditorView();
            else
                StartViewAsync().FireAndForget();
        }
    }

    // Starts an editor view exactly once, deferred until it is connected and knows its pane size, so the
    // engine view opens at the pane resolution and is never restarted mid-session. Idempotent.
    private void TryStartEditorView()
    {
        if (_kind != ViewKind.Editor || _disposed || _startRequested)
            return;
        if (!_engine.IsConnected || Math.Max(_paneWidth, _paneHeight) <= 0)
            return;

        _startRequested = true;
        StartViewAsync().FireAndForget();
    }

    /// <summary>Raised once this stream's engine view has started (its <see cref="ViewName"/> and, for a
    /// preview, its <see cref="WorldAssetId"/> are now known). Fires on every (re)start.</summary>
    public event Action? ViewStarted;

    /// <summary>The preview world's id for an asset-preview view once started (0 otherwise, and after a
    /// stop) — the world the editor provisioned via its provision hook. The asset viewer builds the
    /// previewed entity in this world and frames it.</summary>
    public uint WorldAssetId { get; private set; }

    /// <summary>This stream's engine view name once started (null before, and after a stop). The owning
    /// control filters the globally dispatched <see cref="ViewSurfaceCreated"/> events by this.</summary>
    public string? ViewName => _viewName;

    /// <summary>The engine view starts/stops with the connection.</summary>
    public void Handle(in ConnectionChanged evt)
    {
        if (evt.State == ConnectionState.Connected)
        {
            if (_kind == ViewKind.Editor)
                TryStartEditorView();
            else
                StartViewAsync().FireAndForget();
        }
        else
        {
            // A reconnect re-opens the view (editor views defer again until their size is known).
            _startRequested = false;
            StopViewAsync().FireAndForget();
        }
    }

    /// <summary>
    /// Streams the owning viewport's input to this stream's engine view (a no-op until the view has
    /// started); the engine translates the typed snapshot to its wire format.
    /// </summary>
    public void SendInput(InputSnapshot input)
    {
        if (_viewName is { } name)
            _engine.StreamInput(name, input);
    }

    /// <summary>
    /// Picks the entity under the normalized image point (0..1, top-left origin) in this stream's
    /// view — the click-select query. The engine answers with the nearest entity, a miss, or the
    /// gizmo-handle flag (see <see cref="PickResult"/>).
    /// </summary>
    public async Task<Result<PickResult>> PickAsync(double u, double v)
    {
        if (_viewName is not { } name)
            return Result<PickResult>.Fail("The engine view has not started.");

        return await _engine
            .SendCommandAsync<PickResult>(EngineCommands.ViewPick, new { View = name, U = u, V = v })
            .ContinueOnAnyContext();
    }

    /// <summary>
    /// Projects every entity in this stream's world to its normalized screen position (0..1, top-left
    /// origin) and world-space camera distance — the overlay's per-frame place-and-scale query. Entities
    /// behind the camera are omitted. A no-op failure when the view has not started.
    /// </summary>
    public async Task<Result<IReadOnlyList<EntityScreen>>> ProjectEntitiesAsync()
    {
        if (_viewName is not { } name)
            return Result<IReadOnlyList<EntityScreen>>.Fail("The engine view has not started.");

        var reply = await _engine
            .SendCommandAsync<ProjectEntitiesReply>(EngineCommands.ViewProjectEntities, new { View = name })
            .ContinueOnAnyContext();
        return reply.Success
            ? Result<IReadOnlyList<EntityScreen>>.Ok(reply.Value?.Items ?? [])
            : Result<IReadOnlyList<EntityScreen>>.Fail(reply.Error ?? "Projection failed.");
    }

    // The view.projectEntities reply shape: one screen position per projected entity.
    private sealed record ProjectEntitiesReply(IReadOnlyList<EntityScreen> Items);

    /// <summary>
    /// Frames this asset-preview view's orbit camera to the renderable bounds the editor has built in its
    /// isolated world — called after the previewed entity exists so it opens fully in view. A no-op
    /// failure when this isn't a started preview view.
    /// </summary>
    public async Task<Result> FrameAsync()
    {
        if (WorldAssetId == 0 || !_engine.IsConnected)
            return Result.Fail("The asset preview view has not started.");

        return await _engine
            .SendCommandAsync(EngineCommands.ViewFrameAssetPreview, new { WorldAssetId })
            .ContinueOnAnyContext();
    }

    /// <summary>
    /// Reports the owning viewport's on-screen size in device pixels. An editor view renders at the pane's
    /// resolution (a uniform downscale of the graphics resolution) instead of the full resolution it would
    /// otherwise cover-scale down, so a small pane costs proportionally less to render. The first real size
    /// starts the deferred view; a later size change recreates the view at the new size in the background
    /// (debounced, without a visible gap — see <see cref="RecreateEditorViewAsync"/>). A no-op for
    /// game/preview views, which size to their own content.
    /// </summary>
    public void SetRenderSize(int width, int height)
    {
        if (_kind != ViewKind.Editor || _disposed)
            return;

        var w = Math.Max(0, width);
        var h = Math.Max(0, height);
        if (w == _paneWidth && h == _paneHeight)
            return;
        _paneWidth = w;
        _paneHeight = h;

        // Before the view has started this is the deferred first start at the pane resolution; afterwards
        // it's a resize — debounce it so a drag recreates the view once, when the size settles, not per
        // frame (each recreate is a full engine view start).
        if (!_startRequested)
        {
            TryStartEditorView();
            return;
        }

        _resizeDebounce?.Cancel();
        var cts = new CancellationTokenSource();
        _resizeDebounce = cts;
        RecreateEditorViewAsync(cts.Token).FireAndForget();
    }

    // Recreates the editor view at the current pane size after a resize settles, without ever leaving the
    // compositor acquiring a shared texture whose producer has stopped (that hangs the compositor — the
    // acquire waits forever for a keyed-mutex release that never comes). It starts the replacement view
    // WHILE the old one keeps rendering, flips the active view so the control swaps to the replacement's
    // live surface as soon as its first frame is announced, and only then stops the old view.
    private async Task RecreateEditorViewAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), token).ContinueOnAnyContext();
        }
        catch (OperationCanceledException)
        {
            return; // a newer resize superseded this one
        }

        if (_disposed || token.IsCancellationRequested)
            return;

        string? oldName = null;
        Task<bool>? surfaceReady = null;
        await _gate.WaitAsync().ContinueOnAnyContext();
        try
        {
            if (_disposed || !_engine.IsConnected || _viewName is not { } current)
                return;
            var newLong = Math.Max(_paneWidth, _paneHeight);
            if (newLong <= 0 || Math.Abs(newLong - _appliedLong) <= Math.Max(_appliedLong, 1) * 0.05)
                return; // no meaningful size change since the live view started

            oldName = current;

            // Start the replacement view. The old view is deliberately left running so its shared texture
            // keeps a live producer (its keyed mutex stays satisfied) until the display swaps over.
            var startParams = new { Kind = "editor", RenderWidth = _paneWidth, RenderHeight = _paneHeight };
            var result = await _engine
                .SendCommandAsync<ViewInfo>(EngineCommands.ViewStart, startParams)
                .ContinueOnAnyContext();
            if (_disposed || result is not { Success: true, Value: { } replacement })
            {
                // The replacement failed to start (or we were torn down): keep the old view, and clean up
                // any orphan the engine did create.
                if (result is { Success: true, Value: { } orphan } && _engine.IsConnected)
                    await _engine
                        .SendCommandAsync(EngineCommands.ViewStop, new { Name = orphan.Name })
                        .ContinueOnAnyContext();
                return;
            }

            // Arm the wait for the replacement's first surface, then make it the active view so the owning
            // control imports (swaps to) its live surface the moment it is announced.
            surfaceReady = ArmSurfaceReady(replacement.Name);
            _viewName = replacement.Name;
            _appliedLong = newLong;
        }
        finally
        {
            _gate.Release();
        }

        if (oldName is null || surfaceReady is null)
            return; // returned early inside the lock

        ViewStarted?.Invoke();

        // Wait (bounded) for the replacement surface to be announced and imported so the compositor is
        // acquiring the new, live surface before we stop the old view. A timeout proceeds anyway — a stuck
        // replacement must not leak the old view forever.
        await Task.WhenAny(surfaceReady, Task.Delay(TimeSpan.FromSeconds(3))).ContinueOnAnyContext();
        // A margin for the control's async surface import to bind the new keyed mutex before the old
        // producer goes away.
        await Task.Delay(TimeSpan.FromMilliseconds(250)).ContinueOnAnyContext();

        // Stop the old view (best-effort even if disposed meanwhile, so it can't leak — StopViewAsync only
        // stops the now-active replacement). A no-op once the connection is gone; the engine frees its
        // views on disconnect anyway.
        if (_engine.IsConnected && _viewName != oldName)
            await _engine
                .SendCommandAsync(EngineCommands.ViewStop, new { Name = oldName })
                .ContinueOnAnyContext();
    }

    private Task<bool> ArmSurfaceReady(string name)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _awaitingSurfaceFor = name;
        _surfaceReady = tcs;
        return tcs.Task;
    }

    /// <summary>Trips a pending background recreate's swap once the replacement view's first shared surface
    /// is announced, so the old view is only stopped after the display has moved to the new one.</summary>
    public void Handle(in ViewSurfaceCreated evt)
    {
        if (_awaitingSurfaceFor is { } name && evt.Name == name && evt.Handle != 0)
        {
            _awaitingSurfaceFor = null;
            _surfaceReady?.TrySetResult(true);
        }
    }

    public override void Dispose()
    {
        // The dispatcher outlives the stream, so the base's unregistration matters here — a registered
        // handler list would otherwise keep this (disposed) instance alive.
        base.Dispose();
        _disposed = true;
        _resizeDebounce?.Cancel();
        StopViewAsync().FireAndForget();
    }

    private async Task StartViewAsync()
    {
        var started = false;
        await _gate.WaitAsync().ContinueOnAnyContext();
        try
        {
            if (_disposed)
                return;

            // Stop any previous view first — under this same lock hold (awaited, not fire-and-forget), so
            // a re-entrant start can't leak the old engine view/camera before this stream switches over.
            // A preview view also releases the world it provisioned (world.close) as it goes.
            if (_viewName is { } previous)
            {
                var previousWorld = WorldAssetId;
                _viewName = null;
                WorldAssetId = 0;
                if (_engine.IsConnected)
                    await _engine
                        .SendCommandAsync(EngineCommands.ViewStop, new { Name = previous })
                        .ContinueOnAnyContext();
                await ReleaseProvisionedWorldAsync(previousWorld).ContinueOnAnyContext();
            }

            var kindToken = _kind switch
            {
                ViewKind.Game => "game",
                ViewKind.AssetPreview => "asset",
                _ => "editor",
            };

            // An asset-preview view renders a world the editor provisions for it (registers the preview
            // assets + loads the preview world); provision it now so view.start can bind to it. A missing
            // provision hook or a failed provision leaves the viewport empty rather than starting a broken
            // view. Other kinds bind to the active world (id 0).
            var previewWorld = 0U;
            if (_kind == ViewKind.AssetPreview)
            {
                if (_provisionWorld is null)
                    return;

                var provisioned = await _provisionWorld().ContinueOnAnyContext();
                if (_disposed || !provisioned)
                {
                    // Disposed mid-provision (or provisioning failed): don't leak the world we just loaded.
                    await ReleaseProvisionedWorldAsync(provisioned ? provisioned.Value : 0U)
                        .ContinueOnAnyContext();
                    return;
                }
                previewWorld = provisioned.Value;
            }

            // An asset-preview view carries its provisioned world plus turntable/scale; an editor view
            // carries its on-screen pixel size so the engine renders it at pane resolution; a game view
            // sends only its kind token.
            object startParams = _kind switch
            {
                ViewKind.AssetPreview => new
                {
                    Kind = kindToken, WorldAssetId = previewWorld, Turntable = _turntable, RenderScale = _renderScale,
                },
                ViewKind.Editor => new { Kind = kindToken, RenderWidth = _paneWidth, RenderHeight = _paneHeight },
                _ => new { Kind = kindToken },
            };

            // The engine may have no rendering service or have gone away; the viewport just stays empty.
            var result = await _engine
                .SendCommandAsync<ViewInfo>(EngineCommands.ViewStart, startParams)
                .ContinueOnAnyContext();
            if (_disposed || result is not { Success: true, Value: { } view })
            {
                // Disposed (or torn down) while the call was in flight: don't keep an orphaned engine view
                // or its provisioned preview world.
                if (result is { Success: true, Value: { } orphan } && _engine.IsConnected)
                    await _engine
                        .SendCommandAsync(EngineCommands.ViewStop, new { Name = orphan.Name })
                        .ContinueOnAnyContext();
                await ReleaseProvisionedWorldAsync(previewWorld).ContinueOnAnyContext();
                return;
            }

            _viewName = view.Name;
            if (_kind == ViewKind.Editor)
                _appliedLong = Math.Max(_paneWidth, _paneHeight);
            WorldAssetId = _kind == ViewKind.AssetPreview ? previewWorld : view.WorldAssetId;
            started = true;
        }
        finally
        {
            _gate.Release();
        }

        // Announce outside the gate so a handler (the asset viewer building its preview) can't deadlock
        // against a re-entrant start/stop.
        if (started)
            ViewStarted?.Invoke();
    }

    // Frees the engine-side view (camera + shared texture) for this stream, under the gate. Best-effort:
    // if the connection is already gone the engine tore its views down on disconnect anyway. The start
    // path stops the previous view inline (it must stay under the one lock hold across stop→start), so
    // this is the stop for the external callers (disconnect, dispose).
    private async Task StopViewAsync()
    {
        await _gate.WaitAsync().ContinueOnAnyContext();
        try
        {
            if (_viewName is { } name)
            {
                var world = WorldAssetId;
                _viewName = null;
                WorldAssetId = 0;
                if (_engine.IsConnected)
                    await _engine
                        .SendCommandAsync(EngineCommands.ViewStop, new { Name = name })
                        .ContinueOnAnyContext();
                await ReleaseProvisionedWorldAsync(world).ContinueOnAnyContext();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    // Releases an asset-preview view's provisioned world through the owner's release hook (world.close), so
    // the editor-owned preview world is closed exactly when its view goes away. A no-op for a non-preview
    // stream, an unprovisioned/zero id, or a gone connection (the engine drops its worlds on disconnect).
    private async Task ReleaseProvisionedWorldAsync(uint worldId)
    {
        if (worldId != 0U && _releaseWorld is not null && _engine.IsConnected)
            await _releaseWorld(worldId).ContinueOnAnyContext();
    }
}
