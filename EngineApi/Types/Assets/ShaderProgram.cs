using Toybox.Studio.EngineApi.Types;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.EngineApi.Types.Assets;

/// <summary>
/// The shader stage handles a material's program is built from, mirroring the engine's
/// <c>ShaderProgram</c>. A record value: edit by constructing a changed copy and assigning it back to
/// the owning material, which is what pushes it.
/// </summary>
public sealed record ShaderProgram
{
    public Handle Vertex { get; init; }

    public Handle Fragment { get; init; }

    /// <summary>The optional tessellation control and evaluation stages.</summary>
    public Handle Tesselation { get; init; }

    public Handle Geometry { get; init; }

    public IReadOnlyList<Handle> Computes { get; init; } = [];

    /// <summary>Whether the stage set forms a usable program — compute-only, or at least a vertex and
    /// fragment pair (the engine's <c>ShaderProgram::is_valid</c>).</summary>
    public bool IsValid()
    {
        var hasGraphicsStages =
            Vertex.IsValid || Fragment.IsValid || Tesselation.IsValid || Geometry.IsValid;

        if (Computes.Count > 0)
            return !hasGraphicsStages;

        return Vertex.IsValid && Fragment.IsValid;
    }
}
