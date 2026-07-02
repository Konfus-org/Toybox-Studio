using System.Threading.Tasks;
using Avalonia.Controls;
using Toybox.Studio.Behaviors;
using Toybox.Studio.Utils;

namespace Toybox.Studio.ContextMenu;

/// <summary>
/// App-layer implementation of the low <see cref="IContextMenuOpener"/>: routes a target to its
/// <see cref="ContextMenuCatalog"/> menu and shows the built <see cref="SearchableMenuViewModel"/> in a
/// pointer-anchored flyout. Wired to <see cref="ContextMenuOpener.Current"/> at startup.
/// </summary>
public sealed class ContextMenuOpenerAdapter : IContextMenuOpener
{
    private readonly ContextMenuCatalog _catalog;

    public ContextMenuOpenerAdapter(ContextMenuCatalog catalog) => _catalog = catalog;

    public bool Handles(object target) => _catalog.Handles(target);

    // Awaits the menu build (which may resume off the UI thread after reading the clipboard) and shows the
    // flyout on the UI thread.
    public async Task OpenAsync(Control anchor, object target)
    {
        var menu = await _catalog.BuildAsync(target).ContinueOnAnyContext();
        if (menu is not null)
            Dispatch.To(DispatchContext.UI, () => ShowFlyout(anchor, menu));
    }

    // Hosts a built menu view-model in a pointer-anchored flyout and wires its close/dispose.
    private static void ShowFlyout(Control anchor, SearchableMenuViewModel viewModel)
    {
        var flyout = new Flyout
        {
            Content = new SearchableMenuView { DataContext = viewModel },
            Placement = PlacementMode.Pointer,
        };
        viewModel.CloseRequested += flyout.Hide;
        flyout.Closed += (_, _) => viewModel.Dispose();
        flyout.ShowAt(anchor, showAtPointer: true);
    }
}
