namespace Toybox.Studio.Services.EngineApi;

/// <summary>
/// Which engine camera a viewport stream renders. <see cref="Editor"/> is a free camera spawned at
/// the game camera's position; <see cref="Game"/> mirrors the actual game camera every frame.
/// </summary>
public enum ViewKind
{
    Editor,
    Game,
}
