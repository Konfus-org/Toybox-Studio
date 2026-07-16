using System.Numerics;

namespace Toybox.Studio.PropertyGrid.Slots;

/// <summary>
/// Edits a <see cref="Vector2"/> / <see cref="Vector3"/> / <see cref="Vector4"/> as a row of labeled
/// scalar fields. The whole vector is the accessor's value; a field edit rebuilds it from the current
/// fields and writes it back (there is no per-component storage).
/// </summary>
public sealed class VectorValueViewModel : AxisValueViewModel
{
    private static readonly string[] Labels = ["X", "Y", "Z", "W"];

    private readonly int _count;

    public VectorValueViewModel(PropertyValueAccessor accessor, int count)
        : base(accessor)
    {
        _count = count;
        var components = Read();
        for (var i = 0; i < count; i++)
        {
            var index = i;
            AddAxis(Labels[i], components[i], value => SetComponent(index, value));
        }
    }

    protected override void SyncFromValue()
    {
        var components = Read();
        for (var i = 0; i < Components.Count && i < components.Length; i++)
            Components[i].Sync((decimal)components[i]);
    }

    private void SetComponent(int index, double value)
    {
        var components = new double[_count];
        for (var i = 0; i < _count; i++)
            components[i] = i == index ? value : (double)(Components[i].Value ?? 0m);

        Accessor.Set(Compose(components));
    }

    private double[] Read() => Accessor.Get() switch
    {
        Vector2 v => [v.X, v.Y],
        Vector3 v => [v.X, v.Y, v.Z],
        Vector4 v => [v.X, v.Y, v.Z, v.W],
        _ => new double[_count],
    };

    private object Compose(double[] c) => _count switch
    {
        2 => new Vector2((float)c[0], (float)c[1]),
        4 => new Vector4((float)c[0], (float)c[1], (float)c[2], (float)c[3]),
        _ => new Vector3((float)c[0], (float)c[1], (float)c[2]),
    };
}
