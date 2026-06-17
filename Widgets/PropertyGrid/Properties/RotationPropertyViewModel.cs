using System.Collections.ObjectModel;
using Newtonsoft.Json.Linq;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// Rotation property: the engine stores rotations as a 4-component quaternion <c>[x, y, z, w]</c>, but a
/// raw quaternion is unreadable to edit by hand. This widget presents it as three Euler-angle fields (in
/// degrees, rotation about X/Y/Z), converting back to the quaternion array in place on every edit so the
/// commit closure persists the same shape the engine sent.
/// </summary>
public sealed class RotationPropertyViewModel : PropertyViewModel
{
    private static readonly string[] Labels = ["X", "Y", "Z"];

    // The live [x, y, z, w] array from the backing JSON; we mutate its four elements in place.
    private readonly JArray _quat;

    // Authoritative Euler state (degrees) for the lifetime of this row. Kept here rather than re-derived
    // from the quaternion each edit so the fields don't jump between equivalent angle representations
    // (e.g. across gimbal lock) while the user is typing.
    private readonly double[] _euler = new double[3];

    public RotationPropertyViewModel(PropertyNode node) : base(node)
    {
        _quat = node.Value as JArray ?? [];
        var (x, y, z, w) = ReadQuaternion(_quat);
        (_euler[0], _euler[1], _euler[2]) = ToEulerDegrees(x, y, z, w);

        Components = [];
        for (var index = 0; index < Labels.Length; index++)
            Components.Add(new RotationComponentViewModel(Labels[index], index, _euler, WriteBack));
    }

    public ObservableCollection<RotationComponentViewModel> Components { get; }

    protected override bool SyncCore(PropertyNode node)
    {
        if (node.Value is not JArray array)
            return false;

        // Re-derive the Euler fields from the fresh quaternion. Setting each field's Value updates the shared
        // Euler state and recomposes the (suppressed) backing quaternion, exactly as a user edit would.
        var (x, y, z, w) = ReadQuaternion(array);
        var (degX, degY, degZ) = ToEulerDegrees(x, y, z, w);
        var degrees = new[] { degX, degY, degZ };
        for (var index = 0; index < Components.Count && index < degrees.Length; index++)
            Components[index].Value = (decimal)degrees[index];
        return true;
    }

    /// <summary>
    /// Converts the current Euler angles back to a quaternion and writes it over the backing array, then
    /// raises the commit so the host persists the change.
    /// </summary>
    private void WriteBack()
    {
        var (x, y, z, w) = FromEulerDegrees(_euler[0], _euler[1], _euler[2]);

        // The engine always sends four components; rebuild the array if it arrived malformed.
        while (_quat.Count < 4)
            _quat.Add(new JValue(0.0));
        _quat[0].Replace(new JValue(x));
        _quat[1].Replace(new JValue(y));
        _quat[2].Replace(new JValue(z));
        _quat[3].Replace(new JValue(w));

        RaiseCommit();
    }

    private static (double X, double Y, double Z, double W) ReadQuaternion(JArray array)
    {
        double Component(int index) =>
            index < array.Count ? (double)(PropertyConvert.TryDecimal(array[index]) ?? 0m) : 0.0;

        // No rotation defaults to identity ([0, 0, 0, 1]) rather than the all-zero (degenerate) quat.
        return array.Count < 4
            ? (0.0, 0.0, 0.0, 1.0)
            : (Component(0), Component(1), Component(2), Component(3));
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
