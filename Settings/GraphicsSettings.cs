using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Settings;

/// <summary>
/// The app's global graphics configuration, mirroring the engine's <c>GraphicsSettings</c>. A record
/// value: edit by assigning a changed copy back to the owning <see cref="AppSettings"/>, which is what
/// pushes it.
/// </summary>
public sealed record GraphicsSettings
{
    public VsyncMode VsyncEnabled { get; init; } = VsyncMode.Off;

    public GraphicsApi GraphicsApi { get; init; } = GraphicsApi.Opengl;

    /// <summary>The render resolution — the game window's size and the editor's view textures both
    /// follow it.</summary>
    public Size Resolution { get; init; } = new(1280, 720);

    /// <summary>The nearest cascade's square directional shadow-map resolution in pixels; each further
    /// cascade halves it (the engine floors at 256).</summary>
    public int ShadowMapResolution { get; init; } = 4096;

    /// <summary>How far directional shadows reach, in world units.</summary>
    public float ShadowRenderDistance { get; init; } = 500f;

    /// <summary>The directional shadow filter radius in shadow-map texels.</summary>
    public float ShadowSoftness { get; init; } = 1f;

    /// <summary>How far from the camera point/spot/area lights still light the scene; zero or negative
    /// is unbounded.</summary>
    public float LocalLightMaxDistance { get; init; } = 200f;

    /// <summary>The projected on-screen size in pixels at which an object renders and casts shadows at
    /// full strength; below it the object dithers out.</summary>
    public float MinScreenSize { get; init; } = 3f;

    /// <summary>The fraction of <see cref="MinScreenSize"/> over which a shrinking object fades,
    /// clamped to [0, 1].</summary>
    public float ScreenSizeFadeFraction { get; init; } = 0.5f;
}
