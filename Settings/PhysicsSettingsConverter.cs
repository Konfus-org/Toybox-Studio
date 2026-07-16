using Newtonsoft.Json.Linq;
using System.Numerics;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Settings;

/// <summary>The wire codec for <see cref="PhysicsSettings"/>: an object of camelCase keys, the gravity
/// a bare three-element array. Fields read through <see cref="WireValue.Field"/>, so the engine's
/// serialized dialect (snake_case keys, typed field envelopes) hydrates too.</summary>
public sealed class PhysicsSettingsConverter : IWireConverter<PhysicsSettings>
{
    public PhysicsSettings Read(JToken value)
    {
        if (WireValue.Unwrap(value) is not JObject body)
            return new PhysicsSettings();

        return new PhysicsSettings
        {
            Gravity = WireValue.Field(body, "gravity") is JArray gravity
                ? WireValue.ReadVector3(gravity)
                : new Vector3(0f, -9.81f, 0f),
            FixedTimeStepSeconds = WireValue.ReadSingle(WireValue.Field(body, "fixedTimeStepSeconds"), 1f / 60f),
            MaxSubSteps = WireValue.ReadInt(WireValue.Field(body, "maxSubSteps"), 4),
            MaxBodyCount = WireValue.ReadInt(WireValue.Field(body, "maxBodyCount"), 65536),
            MaxContactConstraints = WireValue.ReadInt(WireValue.Field(body, "maxContactConstraints"), 65536),
            MaxBodyPairs = WireValue.ReadInt(WireValue.Field(body, "maxBodyPairs"), 65536),
            SolverVelocityIterations = WireValue.ReadInt(WireValue.Field(body, "solverVelocityIterations"), 8),
            SolverPositionIterations = WireValue.ReadInt(WireValue.Field(body, "solverPositionIterations"), 2),
            MaxLinearVelocity = WireValue.ReadSingle(WireValue.Field(body, "maxLinearVelocity"), 500f),
            MaxAngularVelocity = WireValue.ReadSingle(WireValue.Field(body, "maxAngularVelocity"), 200f),
        };
    }

    public JToken Write(PhysicsSettings value) => new JObject
    {
        ["gravity"] = WireValue.Write(value.Gravity),
        ["fixedTimeStepSeconds"] = value.FixedTimeStepSeconds,
        ["maxSubSteps"] = value.MaxSubSteps,
        ["maxBodyCount"] = value.MaxBodyCount,
        ["maxContactConstraints"] = value.MaxContactConstraints,
        ["maxBodyPairs"] = value.MaxBodyPairs,
        ["solverVelocityIterations"] = value.SolverVelocityIterations,
        ["solverPositionIterations"] = value.SolverPositionIterations,
        ["maxLinearVelocity"] = value.MaxLinearVelocity,
        ["maxAngularVelocity"] = value.MaxAngularVelocity,
    };
}
