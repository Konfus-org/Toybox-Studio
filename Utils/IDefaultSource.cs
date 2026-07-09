namespace Toybox.Studio.Utils;

/// <summary>
/// A value that supplies its own "default" twin for the property grid's state adorner (the
/// modified/default dot and click-to-reset). The grid normally compares against a
/// default-constructed instance of the edited type; a type whose defaults are data — a keybinding's
/// default is its action's registered chord, not null — implements this instead. Lives in Utils so
/// plain data types can declare it without referencing the grid.
/// </summary>
public interface IDefaultSource
{
    /// <summary>A detached instance carrying this value's default member values.</summary>
    object? CreateDefaults();
}
