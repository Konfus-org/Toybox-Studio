using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Logging;
using Toybox.Studio.Worlds;
using Toybox.Studio.Toolbar;
using Toybox.Studio.Viewport;

namespace Toybox.Studio.Game;

/// <summary>
/// The Game panel: a <see cref="GameViewportViewModel"/> frame (streaming the engine's mirrored game
/// camera and forwarding raw input) plus the Play/Stop/Pause <see cref="Transport"/> — the same
/// data-driven toolbar the viewport uses, seeded with the transport tools (whose game-mode conditions show
/// Play while stopped and Stop/Pause while playing). Single-instance, so its surface streams for the app's
/// lifetime.
/// </summary>
public sealed class GameViewModel : ObservableObject, IDisposable
{
    public GameViewModel(
        Session session, Func<ViewKind, ViewportStream> streamFactory, Logger logger, EngineWatcher watcher,
        ToolCommandRunner toolCommandRunner, ToolbarState toolbarState)
    {
        Transport = new ToolbarViewModel(
            ToolbarLayout.GameTransport(), toolCommandRunner, toolbarState, watcher);
        Viewport = new GameViewportViewModel(session, streamFactory, watcher, logger);
    }

    /// <summary>The game viewport (frame surface + game input policy).</summary>
    public GameViewportViewModel Viewport { get; }

    /// <summary>The Play/Stop/Pause transport toolbar.</summary>
    public ToolbarViewModel Transport { get; }

    public void Dispose()
    {
        Transport.Dispose();
        Viewport.Dispose();
    }
}
