using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json.Linq;
using Toybox.Studio.Services.Motion;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// A 0..1 scalar edited through a clay <c>Slider</c> rather than a numeric field — used for the
/// Accessibility ▸ Animation intensity setting. Mirrors <see cref="NumberPropertyViewModel"/>'s in-place
/// token write (so the settings grid's dirty-tracking, baseline and reset-to-default all work unchanged), and
/// additionally republishes the live motion tokens on every change so dragging the slider previews the new
/// intensity immediately. Wired via [View("intensitySlider")].
/// </summary>
public sealed partial class SliderPropertyViewModel : PropertyViewModel
{
    private readonly JsonValueSlot _slot;

    public SliderPropertyViewModel(PropertyNode node, Action? commit) : base(node)
    {
        CommitChanges = commit;
        _slot = new JsonValueSlot(node.Value);
        _value = (double)(PropertyConvert.TryDecimal(node.Value) ?? 0m);
    }

    [ObservableProperty]
    private double _value;

    partial void OnValueChanged(double value)
    {
        if (_slot.Set(new JValue(value)))
            RaiseCommit();

        // Live preview: the editor's animations react to the new intensity as the slider is dragged, before the
        // value is committed to disk on Save.
        MotionTokens.Publish(value);
    }

    public override JToken? CurrentValue => new JValue(Value);

    public override void ApplyValue(JToken token) => Value = (double)(PropertyConvert.TryDecimal(token) ?? 0m);

    protected override bool SyncCore(PropertyNode node)
    {
        Value = (double)(PropertyConvert.TryDecimal(node.Value) ?? 0m);
        return true;
    }
}
