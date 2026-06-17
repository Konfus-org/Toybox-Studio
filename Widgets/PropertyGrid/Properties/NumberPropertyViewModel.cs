using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json.Linq;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// A numeric property edited through a <c>NumericUpDown</c>. One view-model serves both integral tokens
/// (int/uuid/enum) and floating tokens (float/double); the <paramref name="integer"/> flag is the only
/// difference, picking the display format, the spinner/scrub step, and the cast used when writing the value
/// back (a whole <c>long</c> vs a <c>double</c>). The bound value is <see cref="decimal"/> to match
/// <c>NumericUpDown.Value</c> (the control's universal numeric type) without a converter.
/// </summary>
public sealed partial class NumberPropertyViewModel : PropertyViewModel
{
    private readonly JsonValueSlot _slot;
    private readonly bool _integer;

    public NumberPropertyViewModel(PropertyNode node, bool integer) : base(node)
    {
        _integer = integer;
        _slot = new JsonValueSlot(node.Value);
        _value = PropertyConvert.TryDecimal(node.Value);
    }

    /// <summary>NumericUpDown format string: no decimals for integers, up to six for floats.</summary>
    public string FormatString => _integer ? "0" : "0.######";

    /// <summary>Spinner / drag-scrub step: whole numbers for integers, tenths for floats.</summary>
    public decimal Increment => _integer ? 1m : 0.1m;

    [ObservableProperty]
    private decimal? _value;

    partial void OnValueChanged(decimal? value)
    {
        if (value is null)
            return;

        var token = _integer ? new JValue((long)value.Value) : new JValue((double)value.Value);
        if (_slot.Set(token))
            RaiseCommit();
    }

    public override JToken? CurrentValue =>
        _integer ? new JValue((long)(Value ?? 0m)) : new JValue((double)(Value ?? 0m));

    public override void ApplyValue(JToken token) => Value = PropertyConvert.TryDecimal(token);

    protected override bool SyncCore(PropertyNode node)
    {
        Value = PropertyConvert.TryDecimal(node.Value);
        return true;
    }
}
