using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json.Linq;
using Toybox.Studio.Services.EngineApi;

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

    // uuid/entity-id fields are UNSIGNED 64-bit: ids above long.MaxValue (roughly half the id space) must
    // round-trip through the full ulong range, so they read/write as ulong rather than long. Plain signed
    // integer fields keep their signed behaviour (negative values stay negative). decimal holds either.
    private readonly bool _unsigned;

    [ObservableProperty]
    private decimal? _value;

    public NumberPropertyViewModel(PropertyNode node, bool integer) : base(node)
    {
        _integer = integer;
        _unsigned = node.Type == "uuid";
        _slot = new JsonValueSlot(node.Value);
        _value = PropertyConvert.TryDecimal(node.Value);
    }

    /// <summary>NumericUpDown format string: no decimals for integers, full precision for floats.</summary>
    public string FormatString => _integer ? "0" : "0.#################";

    /// <summary>Spinner / drag-scrub step: whole numbers for integers, tenths for floats.</summary>
    public decimal Increment => _integer ? 1m : 0.1m;

    public override JToken? CurrentValue => MakeToken(Value ?? 0m);

    public override void ApplyValue(JToken token) => Value = PropertyConvert.TryDecimal(token);

    protected override bool SyncCore(PropertyNode node)
    {
        Value = PropertyConvert.TryDecimal(node.Value);
        return true;
    }

    partial void OnValueChanged(decimal? value)
    {
        if (value is null)
            return;

        if (_slot.Set(MakeToken(value.Value)))
            RaiseCommit();
    }

    // An unsigned id casts to ulong (so high-bit ids survive); a signed integer to long; a float keeps full
    // double precision (no decimal/string round-trip that would truncate).
    private JValue MakeToken(decimal value)
    {
        if (!_integer)
            return new JValue((double)value);

        return _unsigned ? new JValue((ulong)value) : new JValue((long)value);
    }
}
