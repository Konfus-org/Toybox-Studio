using Avalonia.Controls;
using System.Threading.Tasks;
using Toybox.Studio.Behaviors;

namespace Toybox.Studio.ContextMenu;

/// <summary>
/// The viewport's context-menu entry point. Generic right-click routing now lives in the low
/// <see cref="ContextMenuOpener"/> attach-behavior; this remains for the viewport, which picks + selects the
/// entity under the cursor first then shows that entity's verbs via a <see cref="ViewportMenuTarget"/>.
/// </summary>
public static class MenuOpenBehavior
{
    /// <summary>
    /// Opens the world menu anchored to <paramref name="anchor"/> at the pointer. The viewport picks + selects
    /// the entity under the cursor first (<paramref name="hitEntity"/> set) then shows that entity's verbs; a
    /// miss (null) shows the same "add / paste" rows as the tree's empty space.
    /// </summary>
    public static Task ShowWorldMenuAsync(Control anchor, ulong? hitEntity) =>
        ContextMenuOpener.Current?.OpenAsync(anchor, new ViewportMenuTarget(hitEntity)) ?? Task.CompletedTask;
}
