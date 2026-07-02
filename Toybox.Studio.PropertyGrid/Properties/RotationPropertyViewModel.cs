using System.Collections.ObjectModel;
using System.Numerics;

namespace Toybox.Studio.PropertyGrid;

/// <summary>
/// Rotation property: the engine stores rotations as a quaternion, but a raw quaternion is unreadable to edit
/// by hand. This widget presents it as three Euler-angle fields (in degrees, rotation about X/Y/Z), converting
/// back to the quaternion on every edit and writing it through the accessor so the commit persists the same
/// value the engine sent.
/// </summary>
public sealed class RotationPropertyViewModel : PropertyViewModel
{
    private static readonly string[] Labels = ["X", "Y", "Z"];

    // Authoritative Euler state (degrees) for the lifetime of this row. Kept here rather than re-derived
    // from the quaternion each edit so the fields don't jump between equivalent angle representations
    // (e.g. across gimbal lock) while the user is typing.
    private readonly double[] _euler = new double[3];

    public RotationPropertyViewModel(PropertyDescriptor descriptor, IValueAccessor accessor)
        : base(descriptor, accessor)
    {
        var (x, y, z, w) = ReadQuaternion();
        (_euler[0], _euler[1], _euler[2]) = ToEulerDegrees(x, y, z, w);

        Components = [];
        for (var index = 0; index < Labels.Length; index++)
            Components.Add(new RotationComponentViewModel(Labels[index], index, _euler, WriteBack));
    }

    public ObservableCollection<RotationComponentViewModel> Components { get; }

    protected override bool SyncCore(IValueAccessor accessor)
    {
        // Re-derive the Euler fields from the fresh quaternion. Setting each field's Value updates the shared
        // Euler state and recomposes the (suppressed) backing quaternion, exactly as a user edit would.
        var q = accessor.Get() as Quaternion? ?? Quaternion.Identity;
        var (degX, degY, degZ) = ToEulerDegrees(q.X, q.Y, q.Z, q.W);
        var degrees = new[] { degX, degY, degZ };
        for (var index = 0; index < Components.Count && index < degrees.Length; index++)
            Components[index].Value = (decimal)degrees[index];
        return true;
    }

    /// <summary>
    /// Converts the current Euler angles back to a quaternion and writes it through the accessor, then raises
    /// the commit so the host persists the change.
    /// </summary>
    private void WriteBack()
    {
        var (x, y, z, w) = FromEulerDegrees(_euler[0], _euler[1], _euler[2]);
        Accessor.Set(new Quaternion((float)x, (float)y, (float)z, (float)w));
        RaiseCommit();
    }

    private (double X, double Y, double Z, double W) ReadQuaternion()
    {
        var q = Accessor.Get() as Quaternion? ?? Quaternion.Identity;
        return (q.X, q.Y, q.Z, q.W);
    }

    /// <summary>
    /// Quaternion (x, y, z, w) to intrinsic ZYX Euler angles in degrees (rotation about X, Y, Z). Matches
    /// <see cref="FromEulerDegrees"/> so the round-trip is stable, with gimbal lock clamped at ±90° pitch.
    /// </summary>
    private static (double X, double Y, double Z) ToEulerDegrees(double x, double y, double z, double w)
    {
        var rollX = Math.Atan2(2.0 * ((w * x) + (y * z)), 1.0 - (2.0 * ((x * x) + (y * y))));

        var sinPitch = 2.0 * ((w * y) - (z * x));
        var pitchY = Math.Abs(sinPitch) >= 1.0
            ? Math.CopySign(Math.PI / 2.0, sinPitch)
            : Math.Asin(sinPitch);

        var yawZ = Math.Atan2(2.0 * ((w * z) + (x * y)), 1.0 - (2.0 * ((y * y) + (z * z))));

        const double toDegrees = 180.0 / Math.PI;
        return (rollX * toDegrees, pitchY * toDegrees, yawZ * toDegrees);
    }

    /// <summary>
    /// Intrinsic ZYX Euler angles in degrees (rotation about X, Y, Z) back to a quaternion (x, y, z, w).
    /// </summary>
    private static (double X, double Y, double Z, double W) FromEulerDegrees(
        double degreesX,
        double degreesY,
        double degreesZ)
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
