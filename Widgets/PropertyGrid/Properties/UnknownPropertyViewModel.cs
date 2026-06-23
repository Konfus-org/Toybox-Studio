using Toybox.Studio.Services.EngineApi;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// Fallback widget: shows the raw JSON for any token without a dedicated widget.
/// </summary>
public sealed class UnknownPropertyViewModel : PropertyViewModel
{
    public UnknownPropertyViewModel(PropertyNode node) : base(node)
    {
        Json = node.Value?.ToString(Newtonsoft.Json.Formatting.None) ?? "";
    }

    public string Json { get; }
}
