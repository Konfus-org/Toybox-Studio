using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Toybox.Studio.Services.EngineApi;
using Toybox.Studio.Services.Logging;
using Toybox.Studio.Widgets.GameToolbar;
using Toybox.Studio.Widgets.Viewport;

namespace Toybox.Studio.Widgets.GameView;

/// <summary>
/// The Game view: shows exactly what the game camera sees (a frame <see cref="Surface"/> streaming the
/// engine's mirrored game camera) and hosts the Play/Stop/Pause <see cref="Transport"/> that used to
/// live in the shell title bar. Single-instance, so its surface streams for the app's lifetime.
/// </summary>
public sealed class GameViewViewModel : ObservableObject, IDisposable
{
    public GameViewViewModel(
        Session session, EngineRpc engine, Logger logger, EngineWatcher watcher, GameToolbarViewModel transport)
    {
        Transport = transport;
        Surface = new ViewportViewModel(session, engine, logger, watcher, ViewKind.Game);
    }

    /// <summary>The frame surface bound to the engine's game-camera mirror.</summary>
    public ViewportViewModel Surface { get; }

    /// <summary>The Play/Stop/Pause transport.</summary>
    public GameToolbarViewModel Transport { get; }

    public void Dispose() => Surface.Dispose();
}
