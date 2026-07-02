namespace Toybox.Studio.EngineApi;

/// <summary>
/// Where a streamed asset's live edits land — the running world the engine keeps it resident in. Streaming isn't
/// preview-specific: an asset can stream into an isolated <see cref="Preview"/> world (the Asset Viewer) or into
/// the <see cref="Game"/> world the editor's viewports render (the active world). The destination only changes the
/// addressing (it picks the <c>stream/{destination}/…</c> root of the <see cref="EngineAddress"/>); the per-field
/// push path is the same uniform <c>reflect.set</c>.
/// </summary>
public enum StreamDestination
{
    /// <summary>The active editing/game world the viewports render (addressed as world 0).</summary>
    Game,

    /// <summary>An isolated preview world the Asset Viewer keeps the asset resident in.</summary>
    Preview,
}
