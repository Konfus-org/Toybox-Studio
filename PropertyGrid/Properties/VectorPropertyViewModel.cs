using System.Collections.ObjectModel;
using System.Globalization;
using System.Numerics;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.PropertyGrid;

/// <summary>
/// Vector property — a row of labeled scalar components. The whole vector is the accessor's CLR value
/// (<c>Vector2</c>/<c>Vector3</c>/<c>Vector4</c>); a component edit rebuilds the vector and writes it back
/// through the accessor (there is no per-element JSON token anymore).
/// </summary>
public sealed class VectorPropertyViewModel : PropertyViewModel
{
    private static readonly string[] Labels = ["X", "Y", "Z", "W"];

    // The component count, fixed by the wire type (vec2/vec3/vec4). The accessor's value is the matching struct.
    private readonly int _count;

    public VectorPropertyViewModel(PropertyDescriptor descriptor, IValueAccessor accessor)
        : base(descriptor, accessor)
    {
        _count = descriptor.Type switch
        {
            EngineTypes.Vec2 => 2,
            EngineTypes.Vec3 => 3,
            EngineTypes.Vec4 => 4,
            _ => 3,
        };

        var components = ReadComponents();
        Components = [];
        for (var index = 0; index < _count; index++)
        {
            var label = index < Labels.Length ? Labels[index] : index.ToString(CultureInfo.InvariantCulture);
            Components.Add(new VectorComponentViewModel(label, index, (decimal)components[index], this));
        }
    }

    public ObservableCollection<VectorComponentViewModel> Components { get; }

    /// <summary>
    /// Applies a single component's value: rebuilds the whole vector from the current component displays and
    /// writes it back through the accessor, then commits. Called by <see cref="VectorComponentViewModel"/>.
    /// </summary>
    public void SetComponent(int index, double value)
    {
        var components = new double[_count];
        for (var i = 0; i < _count; i++)
            components[i] = i == index ? value : (double)(Components[i].Value ?? 0m);

        Accessor.Set(Compose(components));
        RaiseCommit();
    }

    protected override bool SyncCore(IValueAccessor accessor)
    {
        var components = ReadComponents();
        if (components.Length != Components.Count)
            return false;

        // Setting each component's Value updates its display; it will not re-write the accessor during Sync
        // (the base suppresses the commit).
        for (var index = 0; index < Components.Count; index++)
            Components[index].Sync((decimal)components[index]);
        return true;
    }

    private double[] ReadComponents() => Accessor.Get() switch
    {
        Vector2 v => [v.X, v.Y],
        Vector3 v => [v.X, v.Y, v.Z],
        Vector4 v => [v.X, v.Y, v.Z, v.W],
        _ => new double[_count],
    };

    private object Compose(double[] components) => _count switch
    {
        2 => new Vector2((float)components[0], (float)components[1]),
        3 => new Vector3((float)components[0], (float)components[1], (float)components[2]),
        4 => new Vector4((float)components[0], (float)components[1], (float)components[2], (float)components[3]),
        _ => new Vector3((float)components[0], (float)components[1], (float)components[2]),
    };
}
