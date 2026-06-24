using Newtonsoft.Json.Linq;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// Shared helpers for reading JSON tokens into widget values.
/// </summary>
internal static class PropertyConvert
{
    public static decimal? TryDecimal(JToken? token)
    {
        if (token is null)
            return null;

        // A float token that decimal can't represent (NaN/Infinity, or a finite magnitude beyond decimal's
        // ~7.9e28 range) must NOT blank the field: NaN/Infinity has no decimal form (return null but leave the
        // backing value untouched — the caller never overwrites on null), while a finite out-of-range value is
        // clamped to decimal's limits so a legitimate large value still shows rather than vanishing.
        if (token.Type == JTokenType.Float)
        {
            var d = token.Value<double>();
            if (double.IsNaN(d) || double.IsInfinity(d))
                return null;
            if (d > (double)decimal.MaxValue)
                return decimal.MaxValue;
            if (d < (double)decimal.MinValue)
                return decimal.MinValue;
            return (decimal)d;
        }

        try
        {
            return token.Value<decimal>();
        }
        catch
        {
            return null;
        }
    }
}
