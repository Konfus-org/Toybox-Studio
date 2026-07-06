using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Ecs;

/// <summary>
/// A scene camera's projection settings — the perspective toggle, near/far planes, and field of view —
/// plus the render target and viewport it draws into, mirroring the engine <c>Camera</c>'s serialized
/// surface. The aspect ratio is runtime state derived from the target, so it never syncs; the C++
/// <c>set_perspective</c>/<c>set_orthographic</c> helpers are mirrored without their aspect parameter.
/// </summary>
public sealed partial class Camera : Component
{
    public Camera()
    {
        RenderTarget = new RenderTarget();
        Viewport = new Viewport();
        IsPerspective = true;
        ZNear = 0.1f;
        ZFar = 1000.0f;
        Fov = 60.0f;
    }

    /// <summary>The surface the camera renders into; engine-owned (views are assigned engine-side), so
    /// it only ever mirrors in.</summary>
    [EngineSync(EngineCommands.ComponentSet, SyncMode.Mirror, typeof(RenderTargetConverter))]
    public partial RenderTarget RenderTarget { get; private set; }

    [EngineSync(Converter = typeof(ViewportConverter))]
    public partial Viewport Viewport { get; set; }

    [EngineSync]
    public partial bool IsPerspective { get; set; }

    [EngineSync]
    public partial float ZNear { get; set; }

    [EngineSync]
    public partial float ZFar { get; set; }

    /// <summary>The vertical field of view in degrees (the orthographic size when
    /// <see cref="IsPerspective"/> is off).</summary>
    [EngineSync]
    public partial float Fov { get; set; }

    public bool IsOrthographic => !IsPerspective;

    public void SetPerspective(float fov, float zNear, float zFar)
    {
        IsPerspective = true;
        Fov = fov;
        ZNear = zNear;
        ZFar = zFar;
    }

    public void SetOrthographic(float size, float zNear, float zFar)
    {
        IsPerspective = false;
        Fov = size;
        ZNear = zNear;
        ZFar = zFar;
    }
}
