using System.Numerics;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Project;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Ecs.Components;

/// <summary>Typed, engine-synced view of the <c>rigidbody</c> component; edits push live via <c>reflect.set</c>.</summary>
[IconAttribute(Icon.Atom, PaletteColor.Green)]
public sealed partial class Rigidbody : Component
{
    [EngineSync] private float _mass = 1.0f;

    [EngineSync] private bool _isKinematic;

    [EngineSync] private bool _isGravityEnabled = true;

    [EngineSync] private TransformSyncMode _transformSyncMode = TransformSyncMode.Sweep;

    [EngineSync] private Vector3 _linearVelocity;

    [EngineSync] private Vector3 _angularVelocity;

    [EngineSync] private float _friction = 0.5f;

    [EngineSync] private float _restitution;

    [EngineSync] private float _linearDamping = 0.05f;

    [EngineSync] private float _angularDamping = 0.05f;

    [EngineSync] private bool _isSleepEnabled = true;

    [EngineSync] private float _sleepVelocityThreshold = 0.03f;

    [EngineSync] private float _sleepTimeSeconds = 0.5f;
}
