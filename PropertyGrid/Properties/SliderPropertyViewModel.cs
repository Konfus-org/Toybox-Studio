using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json.Linq;
using Toybox.Studio.Behaviors.Animations;

namespace Toybox.Studio.PropertyGrid;

/// <summary>
/// A 0..1 scalar edited through a clay <c>Slider</c> rather than a numeric field — used for the
/// Accessibility ▸ Animation intensity setting. Writes through the accessor like every other leaf (so the
/// settings grid's dirty-tracking, baseline and reset-to-default all work unchanged), and additionally
/// republishes the live motion tokens on every change so dragging the slider previews the new intensity
/// immediately. Wired via [View("intensitySlider")].
/// </summary>
public sealed partial class SliderPropertyViewModel : PropertyViewModel
{
    [ObservableProperty]
    private double _value;

    public SliderPropertyViewModel(PropertyDescriptor descriptor, IValueAccessor accessor)
        : base(descriptor, accessor)
    {
        _value = ReadDouble(accessor.Get());
    }

    public override void ApplyValue(JToken token) => Value = (double)(PropertyConvert.TryDecimal(token) ?? 0m);

    protected override bool SyncCore(IValueAccessor accessor)
    {
        Value = ReadDouble(accessor.Get());
        return true;
    }

    partial void OnValueChanged(double value)
    {
        Accessor.Set(value);
        RaiseCommit();

        // Live preview: the editor's animations react to the new intensity as the slider is dragged, before the
        // value is committed to disk on Save.
        MotionTokens.Publish(value);
    }

    private static double ReadDouble(object? value)
    {
        try
        {
            return value is null ? 0 : Convert.ToDouble(value);
        }
        catch
        {
            return 0;
        }
    }
}
