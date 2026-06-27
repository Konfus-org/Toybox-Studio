using System.Collections.Generic;
using System.Numerics;
using Newtonsoft.Json.Linq;

namespace Toybox.Studio.Services.World.Components;

/// <summary>Typed view of the engine's <c>rigidbody</c> component.</summary>
public sealed record Rigidbody : IComponentType<Rigidbody>
{
    public static string Wire => "rigidbody";

    public float Mass { get; init; } = 1.0f;

    public bool IsKinematic { get; init; }

    public bool IsGravityEnabled { get; init; } = true;

    public TransformSyncMode TransformSyncMode { get; init; } = TransformSyncMode.Sweep;

    public Vector3 LinearVelocity { get; init; } = Vector3.Zero;

    public Vector3 AngularVelocity { get; init; } = Vector3.Zero;

    public float Friction { get; init; } = 0.5f;

    public float Restitution { get; init; }

    public float LinearDamping { get; init; } = 0.05f;

    public float AngularDamping { get; init; } = 0.05f;

    public bool IsSleepEnabled { get; init; } = true;

    public float SleepVelocityThreshold { get; init; } = 0.03f;

    public float SleepTimeSeconds { get; init; } = 0.5f;

    public JObject? Raw { get; init; }

    public JObject ToComponentJson() => ComponentJson.Merge(Raw, new Dictionary<string, JToken>
    {
        ["mass"] = ComponentJson.FloatNode(Mass),
        ["is_kinematic"] = ComponentJson.BoolNode(IsKinematic),
        ["is_gravity_enabled"] = ComponentJson.BoolNode(IsGravityEnabled),
        ["transform_sync_mode"] = ComponentJson.EnumNode(TransformSyncMode),
        ["linear_velocity"] = ComponentJson.Vec3Node(LinearVelocity),
        ["angular_velocity"] = ComponentJson.Vec3Node(AngularVelocity),
        ["friction"] = ComponentJson.FloatNode(Friction),
        ["restitution"] = ComponentJson.FloatNode(Restitution),
        ["linear_damping"] = ComponentJson.FloatNode(LinearDamping),
        ["angular_damping"] = ComponentJson.FloatNode(AngularDamping),
        ["is_sleep_enabled"] = ComponentJson.BoolNode(IsSleepEnabled),
        ["sleep_velocity_threshold"] = ComponentJson.FloatNode(SleepVelocityThreshold),
        ["sleep_time_seconds"] = ComponentJson.FloatNode(SleepTimeSeconds),
    });

    public static Rigidbody FromComponentJson(JObject raw) => new()
    {
        Mass = ComponentJson.ReadFloat(raw["mass"], 1.0f),
        IsKinematic = ComponentJson.ReadBool(raw["is_kinematic"]),
        IsGravityEnabled = ComponentJson.ReadBool(raw["is_gravity_enabled"], true),
        TransformSyncMode = ComponentJson.ReadEnum(raw["transform_sync_mode"], TransformSyncMode.Sweep),
        LinearVelocity = ComponentJson.ReadVector3(raw["linear_velocity"]),
        AngularVelocity = ComponentJson.ReadVector3(raw["angular_velocity"]),
        Friction = ComponentJson.ReadFloat(raw["friction"], 0.5f),
        Restitution = ComponentJson.ReadFloat(raw["restitution"]),
        LinearDamping = ComponentJson.ReadFloat(raw["linear_damping"], 0.05f),
        AngularDamping = ComponentJson.ReadFloat(raw["angular_damping"], 0.05f),
        IsSleepEnabled = ComponentJson.ReadBool(raw["is_sleep_enabled"], true),
        SleepVelocityThreshold = ComponentJson.ReadFloat(raw["sleep_velocity_threshold"], 0.03f),
        SleepTimeSeconds = ComponentJson.ReadFloat(raw["sleep_time_seconds"], 0.5f),
        Raw = raw,
    };
}
