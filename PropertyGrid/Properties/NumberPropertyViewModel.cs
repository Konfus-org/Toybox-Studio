using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.PropertyGrid;

/// <summary>
/// A numeric property edited through a <c>NumericUpDown</c>. One view-model serves both integral tokens
/// (int/uuid/enum) and floating tokens (float/double); the <paramref name="integer"/> flag is the only
/// difference, picking the display format, the spinner/scrub step, and the CLR type written back (a whole
/// <c>long</c>/<c>ulong</c> vs a <c>double</c>). The bound value is <see cref="decimal"/> to match
/// <c>NumericUpDown.Value</c> (the control's universal numeric type) without a converter.
/// </summary>
public sealed partial class NumberPropertyViewModel : PropertyViewModel
{
    private readonly bool _integer;

    // uuid/entity-id fields are UNSIGNED 64-bit: ids above long.MaxValue (roughly half the id space) must
    // round-trip through the full ulong range, so they write as ulong rather than long. Plain signed
    // integer fields keep their signed behaviour (negative values stay negative). decimal holds either.
    private readonly bool _unsigned;

    [ObservableProperty]
    private decimal? _value;

    public NumberPropertyViewModel(PropertyDescriptor descriptor, IValueAccessor accessor, bool integer)
        : base(descriptor, accessor)
    {
        _integer = integer;
        _unsigned = descriptor.Type == EngineTypes.Uuid;
        _value = ReadDecimal(Accessor.Get());
    }

    /// <summary>NumericUpDown format string: no decimals for integers, full precision for floats.</summary>
    public string FormatString => _integer ? "0" : "0.#################";

    /// <summary>Spinner / drag-scrub step: whole numbers for integers, tenths for floats.</summary>
    public decimal Increment => _integer ? 1m : 0.1m;

    public override void ApplyValue(Newtonsoft.Json.Linq.JToken token) => Value = PropertyConvert.TryDecimal(token);

    protected override bool SyncCore(IValueAccessor accessor)
    {
        Value = ReadDecimal(accessor.Get());
        return true;
    }

    partial void OnValueChanged(decimal? value)
    {
        if (value is null)
            return;

        Accessor.Set(Coerce(value.Value));
        RaiseCommit();
    }

    // The CLR value written back: an unsigned id → ulong (so high-bit ids survive), a signed integer → long, a
    // float keeps full double precision (no decimal/string round-trip that would truncate).
    private object Coerce(decimal value)
    {
        if (!_integer)
            return (double)value;

        return _unsigned ? (object)(ulong)value : (long)value;
    }

    private static decimal? ReadDecimal(object? value) => value switch
    {
        null => null,
        decimal d => d,
        _ => TryConvert(value),
    };

    private static decimal? TryConvert(object value)
    {
        try
        {
            return Convert.ToDecimal(value);
        }
        catch
        {
            return null;
        }
    }
}
