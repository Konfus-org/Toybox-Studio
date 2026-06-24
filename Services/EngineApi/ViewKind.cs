namespace Toybox.Studio.Services.EngineApi;

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
