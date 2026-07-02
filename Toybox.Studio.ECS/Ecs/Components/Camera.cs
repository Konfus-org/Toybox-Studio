using Toybox.Studio.EngineApi;
using Toybox.Studio.Viewport;
using Toybox.Studio.Project;
using Toybox.Studio.PropertyGrid;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Ecs.Components;

/// <summary>
/// Typed, engine-synced view of the <c>camera</c> component — its projection settings (perspective toggle, near/far
/// planes, field of view) plus the render target + viewport it draws into. Mirrors the engine's serialized
/// <c>Camera</c> fields.
/// </summary>
[IconAttribute(Icon.Camera, PaletteColor.Green)]
[ViewportIconAttribute(Icon.Camera, PaletteColor.Green)]
public sealed partial class Camera : Component
{
    [EngineSync][ReadOnly] private RenderTarget _renderTarget = new();

    [EngineSync] private Viewport _viewport = new();

    [EngineSync] private bool _isPerspective = true;

    [EngineSync] private float _zNear = 0.1f;

    [EngineSync] private float _zFar = 1000.0f;

    [EngineSync] private float _fov = 60.0f;
}
