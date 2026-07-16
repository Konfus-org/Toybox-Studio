using System.Numerics;

namespace Toybox.Studio.PropertyGrid.Slots;

/// <summary>
/// Edits a <see cref="Quaternion"/> as three Euler-angle fields (degrees, rotation about X / Y / Z) — a
/// raw quaternion is unreadable to author by hand. The authoritative Euler state lives here for the
/// row's lifetime rather than being re-derived from the quaternion on each edit, so the fields don't
/// jump between equivalent angle representations (e.g. across gimbal lock) while one is being typed; an
/// external change (reset, live sync) re-derives them. Intrinsic ZYX convention with a ±90° pitch clamp,
/// matching the engine's quaternion round-trip.
/// </summary>
public sealed class RotationValueViewModel : AxisValueViewModel
{
    private static readonly string[] Labels = ["X", "Y", "Z"];

    private readonly double[] _euler = new double[3];

    public RotationValueViewModel(PropertyValueAccessor accessor)
        : base(accessor)
    {
        (_euler[0], _euler[1], _euler[2]) = ToEulerDegrees(Read());
        for (var i = 0; i < Labels.Length; i++)
        {
            var index = i;
            AddAxis(Labels[i], _euler[i], value => SetAxis(index, value));
        }
    }

    protected override void SyncFromValue()
    {
        (_euler[0], _euler[1], _euler[2]) = ToEulerDegrees(Read());
        for (var i = 0; i < Components.Count; i++)
            Components[i].Sync((decimal)_euler[i]);
    }

    private void SetAxis(int index, double degrees)
    {
        _euler[index] = degrees;
        var (x, y, z, w) = FromEulerDegrees(_euler[0], _euler[1], _euler[2]);
        Accessor.Set(new Quaternion((float)x, (float)y, (float)z, (float)w));
    }

    private Quaternion Read() => Accessor.Get() as Quaternion? ?? Quaternion.Identity;

    // Quaternion → intrinsic ZYX Euler degrees (rotation about X, Y, Z), the inverse of FromEulerDegrees,
    // with gimbal lock clamped at ±90° pitch so the round-trip stays stable.
    private static (double X, double Y, double Z) ToEulerDegrees(Quaternion q)
    {
        double x = q.X, y = q.Y, z = q.Z, w = q.W;
        var rollX = Math.Atan2(2.0 * ((w * x) + (y * z)), 1.0 - (2.0 * ((x * x) + (y * y))));

        var sinPitch = 2.0 * ((w * y) - (z * x));
        var pitchY = Math.Abs(sinPitch) >= 1.0
            ? Math.CopySign(Math.PI / 2.0, sinPitch)
            : Math.Asin(sinPitch);

        var yawZ = Math.Atan2(2.0 * ((w * z) + (x * y)), 1.0 - (2.0 * ((y * y) + (z * z))));

        const double toDegrees = 180.0 / Math.PI;
        return (rollX * toDegrees, pitchY * toDegrees, yawZ * toDegrees);
    }

    // Intrinsic ZYX Euler degrees (rotation about X, Y, Z) → quaternion (x, y, z, w).
    private static (double X, double Y, double Z, double W) FromEulerDegrees(
        double degreesX, double degreesY, double degreesZ)
    {
        const double toRadians = Math.PI / 180.0;
        var halfRoll = degreesX * toRadians * 0.5;
        var halfPitch = degreesY * toRadians * 0.5;
        var halfYaw = degreesZ * toRadians * 0.5;

        var cr = Math.Cos(halfRoll);
        var sr = Math.Sin(halfRoll);
        var cp = Math.Cos(halfPitch);
        var sp = Math.Sin(halfPitch);
        var cy = Math.Cos(halfYaw);
        var sy = Math.Sin(halfYaw);

        var w = (cr * cp * cy) + (sr * sp * sy);
        var x = (sr * cp * cy) - (cr * sp * sy);
        var y = (cr * sp * cy) + (sr * cp * sy);
        var z = (cr * cp * sy) - (sr * sp * cy);
        return (x, y, z, w);
    }
}
