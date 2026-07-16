using System;
using Toybox.Studio.EngineApi.Types;
using Toybox.Studio.PropertyGrid;
using Toybox.Studio.PropertyGrid.Slots;

namespace Toybox.Studio.Settings;

/// <summary>
/// Edits a <see cref="Size"/> (a width/height pixel pair, e.g. the render resolution) as two labeled
/// integer fields, reusing the grid's shared multi-field axis editor (and its <c>AxisValueView</c>, matched
/// by the <see cref="AxisValueViewModel"/> base). The whole <see cref="Size"/> is the accessor's value; a
/// field edit rebuilds it from both fields and writes it back. Without this a <see cref="Size"/> — a
/// value-type record the reflection grid can't recurse into — fell through to the raw-JSON fallback.
/// </summary>
public sealed class SizeValueViewModel : AxisValueViewModel
{
    public SizeValueViewModel(PropertyValueAccessor accessor)
        : base(accessor)
    {
        var size = Read();
        AddAxis("W", size.Width, value => SetComponent(index: 0, value));
        AddAxis("H", size.Height, value => SetComponent(index: 1, value));
    }

    protected override void SyncFromValue()
    {
        var size = Read();
        if (Components.Count >= 2)
        {
            Components[0].Sync(size.Width);
            Components[1].Sync(size.Height);
        }
    }

    private void SetComponent(int index, double value)
    {
        // Read the sibling field from the current display (as VectorValueViewModel does) so a single-field
        // edit keeps the other dimension, then round to the integer pixel counts a Size carries.
        var width = index == 0 ? value : (double)(Components[0].Value ?? 0m);
        var height = index == 1 ? value : (double)(Components[1].Value ?? 0m);
        Accessor.Set(new Size(Round(width), Round(height)));
    }

    private Size Read() => Accessor.Get() as Size? ?? default;

    private static int Round(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);
}
