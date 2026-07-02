namespace Toybox.Studio.PropertyGrid;

/// <summary>
/// A theme selector rendered as a dropdown of theme names (supplied as the descriptor's <c>$choices</c> from
/// the themes folder). Commits the chosen name back through the accessor; the settings grid applies it. Wired
/// via [View("themePicker")]. Kept as its own widget so it can grow (e.g. show swatches) without touching the
/// generic enum widget.
/// </summary>
public sealed class ThemePickerPropertyViewModel : DropdownPropertyViewModel
{
    public ThemePickerPropertyViewModel(PropertyDescriptor descriptor, IValueAccessor accessor)
        : base(descriptor, accessor)
    {
    }
}
