using System.Threading.Tasks;
using Avalonia.Controls;

namespace Toybox.Studio.Behaviors;

/// <summary>
/// Builds and shows the context menu registered for a target object. Inverts the low
/// <see cref="ContextMenuOpener"/> attach-behavior's dependency on the app-layer context-menu system: the app
/// supplies the implementation (which knows the concrete menus and the searchable-menu flyout), and the
/// behavior just routes right-clicks to it.
/// </summary>
public interface IContextMenuOpener
{
    /// <summary>Whether a menu is registered for <paramref name="target"/>'s type.</summary>
    bool Handles(object target);

    /// <summary>Builds the menu for <paramref name="target"/> and shows it anchored at <paramref name="anchor"/>.</summary>
    Task OpenAsync(Control anchor, object target);
}
