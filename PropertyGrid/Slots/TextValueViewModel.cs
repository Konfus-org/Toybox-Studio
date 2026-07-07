namespace Toybox.Studio.PropertyGrid.Slots;

/// <summary>
/// Edits a string value in a text box. Also the display-only fallback for value shapes without a
/// dedicated editor — the factory hands those a read-only accessor and this shows their ToString.
/// </summary>
public sealed class TextValueViewModel(PropertyValueAccessor accessor) : ValueViewModel(accessor)
{
    public string Value
    {
        get => Accessor.Get()?.ToString() ?? string.Empty;
        set => Accessor.Set(value);
    }
}
