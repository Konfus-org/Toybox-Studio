namespace Toybox.Studio.PropertyGrid.Slots;

/// <summary>
/// The indicator for an <see cref="UnknownValueViewModel"/> row: a small "?" badge whose tooltip explains
/// that the property's type has no dedicated editor and is edited as raw JSON. It stands in for the
/// default/modified state dot, which an unsupported type has no default twin to drive.
/// </summary>
public sealed class UnknownIndicatorViewModel(Type valueType)
{
    public string Tip =>
        $"'{valueType.Name}' isn't supported by the property grid — edit its raw JSON directly.";
}
