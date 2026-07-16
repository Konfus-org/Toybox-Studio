using Toybox.Studio.EngineApi.Types;
namespace Toybox.Studio.EngineApi.Types.Assets;

/// <summary>Whether a model's geometry is shared/immutable or runtime-mutable, mirroring the engine's
/// <c>MeshMode</c>.</summary>
public enum MeshMode
{
    Static = 0,
    Dynamic = 1,
}
