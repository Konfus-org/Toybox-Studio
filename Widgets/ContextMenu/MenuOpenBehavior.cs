using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Toybox.Studio.Services.Commands;
using Toybox.Studio.Utils;
using Toybox.Studio.Widgets.Ecs;
using Toybox.Studio.Widgets.PropertyGrid;
using Toybox.Studio.Widgets.Viewport;

namespace Toybox.Studio.Widgets.ContextMenu;

/// <summary>
/// Opens a code-driven context menu on right-click. Set <c>MenuOpenBehavior.Menu="entity"</c> (etc.) on any
/// control and a right-click (or the context-menu key) builds that menu — for the right-clicked entity, the
/// targeted component, a property row, or empty space — and shows it in a flyout at the pointer. The handler is
/// registered on the <b>tunnel</b> route so it fires before the event reaches a descendant editor (a
/// <c>TextBox</c>/<c>NumericUpDown</c> in a property row), claiming the gesture so the editor's native text menu
/// never preempts the property menu. The target is derived from the control's <c>DataContext</c>: an
/// <see cref="EntityViewModel"/> (or viewport <see cref="BillboardViewModel"/>) is selected first so the menu
/// acts on it, a <see cref="ComponentViewModel"/> targets that component, a <see cref="PropertyViewModel"/>
/// builds the property row's menu, and anything else opens the background ("add / paste here") menu.
/// </summary>
public static class MenuOpenBehavior
{
    public static readonly AttachedProperty<string?> MenuProperty =
        AvaloniaProperty.RegisterAttached<Control, string?>("Menu", typeof(MenuOpenBehavior));

    static MenuOpenBehavior() => MenuProperty.Changed.AddClassHandler<Control>(OnMenuChanged);

    public static void SetMenu(Control control, string? value) => control.SetValue(MenuProperty, value);

    public static string? GetMenu(Control control) => control.GetValue(MenuProperty);

    /// <summary>
    /// Opens the entity menu (when an entity was hit / is selected) or the background menu, anchored to
    /// <paramref name="anchor"/> at the pointer. Used by the viewport, which picks + selects the entity under
    /// the cursor first, then shows that entity's menu.
    /// </summary>
    public static Task ShowEntityOrBackgroundAsync(Control anchor, bool hasEntity)
    {
        if (ContextMenuService.Current is not { } service)
            return Task.CompletedTask;

        return BuildAndShowAsync(
            anchor, hasEntity ? service.BuildEntityMenuAsync() : service.BuildBackgroundMenuAsync());
    }

    private static void OnMenuChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        control.RemoveHandler(InputElement.ContextRequestedEvent, OnContextRequested);
        if (e.NewValue is string id && id.Length > 0)
            control.AddHandler(
                InputElement.ContextRequestedEvent, OnContextRequested,
                RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private static void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not Control control || GetMenu(control) is not { Length: > 0 } host
            || ContextMenuService.Current is null)
            return;

        // Claim the gesture before the async build so a descendant editor's native menu — or an ancestor's
        // menu — never also opens. Tunnelling means this runs before the event reaches the editor.
        e.Handled = true;
        ShowForAsync(control, host).FireAndForget();
    }

    private static Task ShowForAsync(Control control, string host)
    {
        if (ContextMenuService.Current is not { } service)
            return Task.CompletedTask;

        // The property row's menu acts on its own view-model, not the world selection.
        if (control.DataContext is PropertyViewModel property)
            return BuildAndShowAsync(control, service.BuildPropertyAsync(property));

        // BuildContext selects the right-clicked entity first, so the menu's actions act on what was clicked.
        var context = BuildContext(control, host);
        var build = host switch
        {
            MenuHosts.Component => service.BuildComponentMenuAsync(context),
            MenuHosts.Background => service.BuildBackgroundMenuAsync(),
            _ when context.IsBackground => service.BuildBackgroundMenuAsync(),
            _ => service.BuildEntityMenuAsync(),
        };
        return BuildAndShowAsync(control, build);
    }

    // Awaits the menu build (which may resume off the UI thread after reading the clipboard) and shows the
    // flyout on the UI thread.
    private static async Task BuildAndShowAsync(Control anchor, Task<SearchableMenuViewModel?> build)
    {
        var menu = await build.ContinueOnAnyContext();
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

    private static MenuContext BuildContext(Control control, string menuId)
    {
        var selection = ContextMenuService.Current!.Selection;
        switch (control.DataContext)
        {
            case EntityViewModel entity:
                // Right-clicking selects the entity (unless it's already part of a multi-selection), so the
                // menu's actions act on what the user clicked.
                if (!selection.Contains(entity.Id))
                    selection.Select(entity.Id);
                return new MenuContext { Host = menuId, EntityId = entity.Id };

            case BillboardViewModel billboard:
                // A viewport billboard icon: select that entity and open its menu, like a tree row.
                if (!selection.Contains(billboard.Id))
                    selection.Select(billboard.Id);
                return new MenuContext { Host = menuId, EntityId = billboard.Id };

            case ComponentViewModel component:
                return new MenuContext
                {
                    Host = menuId,
                    EntityId = selection.PrimaryId,
                    Component = component.Name,
                };

            default:
                return new MenuContext { Host = menuId, IsBackground = true };
        }
    }
}
