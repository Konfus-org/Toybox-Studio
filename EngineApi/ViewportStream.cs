using Toybox.Studio.AppHosting;
using Toybox.Studio.Events;
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

/// <summary>The engine's reply to view.start: the new view's id.</summary>
public sealed record ViewInfo(string Name);

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
public sealed class ViewportStream : EventSubscriber, IEventHandler<ConnectionChanged>
{
    private readonly Engine _engine;
    private readonly ViewKind _kind;

    // Serializes start/stop so overlapping StartViewAsync calls (e.g. a reconnect racing the mid-session
    // open) can't both run StartView and leak an engine view, and so _viewName is never written
    // concurrently. A start always stops the previous view (and awaits its view.stop) before switching.
    private readonly SemaphoreSlim _gate = new(1, 1);

    private volatile string? _viewName;
    private bool _disposed;

    public ViewportStream(Engine engine, EventDispatcher events, ViewKind kind = ViewKind.Editor)
        : base(events)
    {
        _engine = engine;
        _kind = kind;

        // Opened mid-session (e.g. a new viewport while the engine is already running): start
        // right away rather than waiting for the next connect.
        if (engine.IsConnected)
            StartViewAsync().FireAndForget();
    }

    /// <summary>This stream's engine view name once started (null before, and after a stop). The owning
    /// control filters the globally dispatched <see cref="ViewSurfaceCreated"/> events by this.</summary>
    public string? ViewName => _viewName;

    /// <summary>The engine view starts/stops with the connection.</summary>
    public void Handle(in ConnectionChanged evt)
    {
        if (evt.State == ConnectionState.Connected)
            StartViewAsync().FireAndForget();
        else
            StopViewAsync().FireAndForget();
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

    public override void Dispose()
    {
        // The dispatcher outlives the stream, so the base's unregistration matters here — a registered
        // handler list would otherwise keep this (disposed) instance alive.
        base.Dispose();
        _disposed = true;
        StopViewAsync().FireAndForget();
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

            var kindToken = _kind switch
            {
                ViewKind.Game => "game",
                ViewKind.AssetPreview => "asset",
                _ => "editor",
            };

            // The engine may have no rendering service or have gone away; the viewport just stays empty.
            var result = await _engine
                .SendCommandAsync<ViewInfo>(EngineCommands.ViewStart, new { Kind = kindToken })
                .ContinueOnAnyContext();
            if (_disposed || result is not { Success: true, Value: { } view })
            {
                // Disposed (or torn down) while the call was in flight: don't keep an orphaned engine view.
                if (result is { Success: true, Value: { } orphan } && _engine.IsConnected)
                    await _engine
                        .SendCommandAsync(EngineCommands.ViewStop, new { Name = orphan.Name })
                        .ContinueOnAnyContext();
                return;
            }

            _viewName = view.Name;
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
        // Free the engine-side view (camera + shared texture) for this stream. Awaited (not fire-and-forget)
        // so the old view.stop is actually sent before a following StartView, preventing a leaked view.
        // Best-effort: if the connection is already gone the engine tore its views down on disconnect anyway.
        if (_viewName is { } name)
        {
            _viewName = null;
            if (_engine.IsConnected)
                await _engine.SendCommandAsync(EngineCommands.ViewStop, new { Name = name }).ContinueOnAnyContext();
        }
    }
}
