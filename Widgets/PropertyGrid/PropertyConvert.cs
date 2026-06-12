using Newtonsoft.Json.Linq;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// Shared helpers for reading JSON tokens into widget values.
/// </summary>
internal static class PropertyConvert
{
    public static decimal? TryDecimal(JToken? token)
    {
        try
        {
            return token?.Value<decimal>();
        }
        catch
        {
            return null;
        }
    }
}
