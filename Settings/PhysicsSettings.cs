using System.Numerics;

namespace Toybox.Studio.Settings;

/// <summary>
/// The app's global physics simulation configuration, mirroring the engine's <c>PhysicsSettings</c>.
/// A record value: edit by assigning a changed copy back to the owning <see cref="AppSettings"/>,
/// which is what pushes it.
/// </summary>
public sealed record PhysicsSettings
{
    public Vector3 Gravity { get; init; } = new(0f, -9.81f, 0f);

    public float FixedTimeStepSeconds { get; init; } = 1f / 60f;

    public int MaxSubSteps { get; init; } = 4;

    public int MaxBodyCount { get; init; } = 65536;

    public int MaxContactConstraints { get; init; } = 65536;

    public int MaxBodyPairs { get; init; } = 65536;

    public int SolverVelocityIterations { get; init; } = 8;

    public int SolverPositionIterations { get; init; } = 2;

    public float MaxLinearVelocity { get; init; } = 500f;

    public float MaxAngularVelocity { get; init; } = 200f;
}
