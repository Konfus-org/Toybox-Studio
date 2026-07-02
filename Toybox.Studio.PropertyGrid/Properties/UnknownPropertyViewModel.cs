using Toybox.Studio.EngineApi;

namespace Toybox.Studio.PropertyGrid;

/// <summary>
/// Fallback widget: shows the raw JSON for any value without a dedicated widget.
/// </summary>
public sealed class UnknownPropertyViewModel : PropertyViewModel
{
    public UnknownPropertyViewModel(PropertyDescriptor descriptor, IValueAccessor accessor)
        : base(descriptor, accessor)
    {
        var value = accessor.Get();
        Json = value is null ? "" : EngineSyncValue.WriteBare(value).ToString(Newtonsoft.Json.Formatting.None);
    }

    public string Json { get; }
}
