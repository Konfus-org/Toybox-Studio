using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Ecs;

/// <summary>
/// The shared contact behavior of the shape-specific solid colliders (<see cref="BoxCollider"/>,
/// <see cref="SphereCollider"/>, <see cref="CapsuleCollider"/>, <see cref="MeshCollider"/>), mirroring
/// the engine's <c>Collider</c> — never attached to an entity directly, so abstract here. The engine's
/// contact callbacks surface as the synced events (streamed only while handlers are attached, and only
/// at play time — the editing world never simulates).
/// </summary>
public abstract partial class Collider : Component
{
    /// <summary>This collider's body started touching another solid body. Raised on the UI thread.</summary>
    [EngineSync(Converter = typeof(ContactConverter))]
    public partial event Action<Contact> CollisionBegan;

    /// <summary>This collider's body stopped touching another solid body; the contact's
    /// position/normal are zero — only the pair is known at separation. Raised on the UI thread.</summary>
    [EngineSync(Converter = typeof(ContactConverter))]
    public partial event Action<Contact> CollisionEnded;
}
