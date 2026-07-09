using Toybox.Studio.PropertyGrid.Slots;

namespace Toybox.Studio.PropertyGrid;

/// <summary>
/// One pluggable value-slot editor for the <see cref="ReflectionPropertyNodeFactory"/>: a domain
/// hands the factory editors for the leaf shapes the grid doesn't know (a keybinding chord, …), and
/// matching properties get the editor's view-model instead of read-only text. The hosting view
/// supplies the matching DataTemplate (slot views resolve through the visual tree, so a template
/// registered above the grid reaches every row) — the grid itself stays domain-free.
/// </summary>
public interface IValueEditor
{
    /// <summary>Whether this editor edits values of the (nullable-unwrapped) type.</summary>
    bool CanEdit(Type editType);

    ValueViewModel CreateEditor(PropertyValueAccessor accessor, Type editType);
}
