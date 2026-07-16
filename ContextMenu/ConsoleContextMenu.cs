using System.Threading.Tasks;
using Toybox.Studio.Console;
using Toybox.Studio.Events;
using Toybox.Studio.Favorites;
using Toybox.Studio.Utils;

namespace Toybox.Studio.ContextMenu;

/// <summary>
/// The console's right-click menu (any console — it's routed at the generic <see cref="ConsoleViewModel"/>):
/// copy the selection (or the whole log when nothing is selected), select all, and clear. A read-only stream,
/// so there's no paste. Search and favorites come for free from the shared searchable menu surface.
/// </summary>
public sealed class ConsoleContextMenu : ContextMenu<ConsoleViewModel>
{
    public ConsoleContextMenu(FavoritesManager favorites, EventDispatcher events)
        : base(favorites, events)
    {
    }

    protected override Task Build(MenuBuilder menu, ConsoleViewModel target)
    {
        menu.Item("Copy", Icon.Copy).Gesture("Ctrl+C").Run(() => target.CopyAsync());
        menu.Item("Select All", Icon.TextSelect).Gesture("Ctrl+A").Run(target.SelectAll);
        menu.Separator();
        menu.Item("Clear", Icon.Trash2).Color(Palette.Red).Run(target.Clear);
        return Task.CompletedTask;
    }
}
