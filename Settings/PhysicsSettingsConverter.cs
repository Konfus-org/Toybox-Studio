using System.Numerics;
using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Settings;

/// <summary>The wire codec for <see cref="PhysicsSettings"/>: an object of camelCase keys, the gravity
/// a bare three-element array.</summary>
public sealed class PhysicsSettingsConverter : IWireConverter<PhysicsSettings>
{
    public PhysicsSettings Read(JToken value)
    {
        if (value is not JObject body)
            return new PhysicsSettings();

        return new PhysicsSettings
        {
            Gravity = body["gravity"] is JArray gravity
                ? WireValue.ReadVector3(gravity)
                : new Vector3(0f, -9.81f, 0f),
            FixedTimeStepSeconds = WireValue.ReadSingle(body["fixedTimeStepSeconds"], 1f / 60f),
            MaxSubSteps = WireValue.ReadInt(body["maxSubSteps"], 4),
            MaxBodyCount = WireValue.ReadInt(body["maxBodyCount"], 65536),
            MaxContactConstraints = WireValue.ReadInt(body["maxContactConstraints"], 65536),
            MaxBodyPairs = WireValue.ReadInt(body["maxBodyPairs"], 65536),
            SolverVelocityIterations = WireValue.ReadInt(body["solverVelocityIterations"], 8),
            SolverPositionIterations = WireValue.ReadInt(body["solverPositionIterations"], 2),
            MaxLinearVelocity = WireValue.ReadSingle(body["maxLinearVelocity"], 500f),
            MaxAngularVelocity = WireValue.ReadSingle(body["maxAngularVelocity"], 200f),
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
