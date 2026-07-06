namespace Toybox.Studio.Assets;

/// <summary>The render role a material plays, mirroring the engine's <c>MaterialType</c> — systems
/// (and the editor) reason about a material without inspecting its shader or config; a sky material,
/// for instance, previews as the environment background rather than on a mesh.</summary>
public enum MaterialType
{
    /// <summary>A standard rasterized surface drawn on mesh geometry.</summary>
    Raster = 0,

    /// <summary>An environment/background material (skybox or sky-sphere).</summary>
    Sky = 1,

    /// <summary>A full-screen post-process effect.</summary>
    Post = 2,

    /// <summary>A geometry/depth pass (e.g. shadow or depth pre-pass).</summary>
    Geo = 3,

    /// <summary>A compute-shader material (no rasterized surface).</summary>
    Compute = 4,
}
