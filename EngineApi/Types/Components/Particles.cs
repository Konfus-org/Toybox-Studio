using Toybox.Studio.EngineApi.Types.Assets;
using Toybox.Studio.EngineApi.Types;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.EngineApi.Types.Components;

/// <summary>A particle emitter's material and source model, mirroring the engine's (still skeletal)
/// <c>Particles</c> — its settings block is a TODO engine-side and grows here as it lands.</summary>
public sealed partial class Particles : Component
{
    /// <summary>The particle material (<c>.mat</c>).</summary>
    [EngineSync]
    public partial Handle Material { get; set; }

    /// <summary>The model particles are emitted as.</summary>
    [EngineSync]
    public partial Handle Model { get; set; }
}
