namespace Toybox.Studio.WorldViewer;

/// <summary>
/// Which pipeline intermediate the editor viewports render as their output — the render-layers
/// toolbar's stage radio row. <see cref="Final"/> is the normal shaded frame; the rest swap in one
/// raw intermediate (post-processing is bypassed for them engine-side). Mirrors the engine's
/// RenderDebugStage; the wire names ride on <see cref="RenderLayers"/>' push.
/// </summary>
public enum RenderStage
{
    Final,
    Diffuse,
    Normals,
    Shadows,
    Depth,
}
