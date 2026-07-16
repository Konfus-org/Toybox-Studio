using Toybox.Studio.EngineApi.Types.Assets;
using Toybox.Studio.EngineApi.Types;
namespace Toybox.Studio.EngineApi.Types.Components;

/// <summary>The runtime mesh the sky is rendered on, mirroring the engine's <c>SkyType</c>.</summary>
public enum SkyType
{
    Box = 0,
    Sphere = 1,
}
