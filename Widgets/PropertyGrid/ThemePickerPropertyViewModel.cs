namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// A theme selector rendered as a dropdown of theme names (supplied as the node's <c>$choices</c> from
/// the themes folder). Commits the chosen name back to the backing token; the settings grid applies it.
/// Wired via [View("themePicker")]. Kept as its own widget so it can grow (e.g. show swatches) without
/// touching the generic enum widget.
/// </summary>
public sealed class ThemePickerPropertyViewModel : DropdownPropertyViewModel
{
    public ThemePickerPropertyViewModel(PropertyNode node, Action? commit) : base(node)
    {
        CommitChanges = commit;
    }
}
