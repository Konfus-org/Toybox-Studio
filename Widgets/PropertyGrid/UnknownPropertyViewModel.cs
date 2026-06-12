using Toybox.Studio.Services;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// Fallback widget: shows the raw JSON for any token without a dedicated widget.
/// </summary>
public sealed class UnknownPropertyViewModel : PropertyViewModelBase
{
    public UnknownPropertyViewModel(PropertyNode node) : base(node)
    {
        Json = node.Value?.ToString(Newtonsoft.Json.Formatting.None) ?? "";
    }

    public string Json { get; }
}
