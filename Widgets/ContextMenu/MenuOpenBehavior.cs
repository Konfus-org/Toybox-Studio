using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Toybox.Studio.Services.Commands;
using Toybox.Studio.Widgets.Ecs;
using Toybox.Studio.Widgets.PropertyGrid;
using Toybox.Studio.Widgets.Viewport;

namespace Toybox.Studio.Widgets.ContextMenu;

/// <summary>
/// Opens a data-driven context menu on right-click. Set <c>MenuOpenBehavior.Menu="entity"</c> (etc.) on any
/// control and a right-click (or the context-menu key) builds the menu for that id and shows it in a flyout at
/// the pointer. The target is derived from the control's <c>DataContext</c>: an <see cref="EntityViewModel"/> is
/// selected first (so the menu's <c>editor.*</c> verbs act on it), a <see cref="ComponentViewModel"/> targets
/// that component on the selected entity, and anything else opens a background ("add / paste here") menu. The
/// viewport opens its menu through the static <see cref="Show"/> helper instead, since its right-button is also
/// the camera-pan gesture.
/// </summary>
public static class MenuOpenBehavior
{
    public static readonly AttachedProperty<string?> MenuProperty =
        AvaloniaProperty.RegisterAttached<Control, string?>("Menu", typeof(MenuOpenBehavior));

    /// <summary>Opens the named menu on a left-click (Tapped) instead of right-click — for a toolbar button.</summary>
    public static readonly AttachedProperty<string?> ClickMenuProperty =
        AvaloniaProperty.RegisterAttached<Control, string?>("ClickMenu", typeof(MenuOpenBehavior));

    static MenuOpenBehavior()
    {
        MenuProperty.Changed.AddClassHandler<Control>(OnMenuChanged);
        ClickMenuProperty.Changed.AddClassHandler<Control>(OnClickMenuChanged);
    }

    public static void SetMenu(Control control, string? value) => control.SetValue(MenuProperty, value);

    public static string? GetMenu(Control control) => control.GetValue(MenuProperty);

    public static void SetClickMenu(Control control, string? value) =>
        control.SetValue(ClickMenuProperty, value);

    public static string? GetClickMenu(Control control) => control.GetValue(ClickMenuProperty);

    /// <summary>
    /// Builds and shows the <paramref name="menuId"/> menu over <paramref name="context"/>, anchored to
    /// <paramref name="anchor"/> at the pointer. Returns false (showing nothing) when the menu service isn't
    /// ready or the menu resolves empty. The flyout disposes the menu view-model on close.
    /// </summary>
    public static bool Show(Control anchor, string menuId, MenuContext context) =>
        ContextMenuService.Current?.Build(menuId, context) is { } viewModel && ShowFlyout(anchor, viewModel);

    // Hosts a built menu view-model in a pointer-anchored flyout and wires its close/dispose.
    private static bool ShowFlyout(Control anchor, SearchableMenuViewModel viewModel)
    {
        var flyout = new Flyout
        {
            Content = new SearchableMenuView { DataContext = viewModel },
            Placement = PlacementMode.Pointer,
        };
        viewModel.CloseRequested += flyout.Hide;
        flyout.Closed += (_, _) => viewModel.Dispose();
        flyout.ShowAt(anchor, showAtPointer: true);
        return true;
    }

    private static void OnMenuChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        control.ContextRequested -= OnContextRequested;
        if (e.NewValue is string id && id.Length > 0)
            control.ContextRequested += OnContextRequested;
    }

    private static void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not Control control || GetMenu(control) is not { Length: > 0 } menuId)
            return;

        // A property-grid row builds its menu from its own view-model (copy/paste value, reset), since those
        // act on that specific row rather than the global selection.
        if (control.DataContext is PropertyViewModel property)
        {
            if (ContextMenuService.Current?.BuildProperty(property) is { } menu && ShowFlyout(control, menu))
                e.Handled = true;
            return;
        }

        if (Show(control, menuId, BuildContext(control, menuId)))
            e.Handled = true;
    }

    private static void OnClickMenuChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        control.Tapped -= OnTapped;
        if (e.NewValue is string id && id.Length > 0)
            control.Tapped += OnTapped;
    }

    private static void OnTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control control && GetClickMenu(control) is { Length: > 0 } menuId)
            Show(control, menuId, new MenuContext { Host = menuId });
    }

    private static MenuContext BuildContext(Control control, string menuId)
    {
        var selection = ContextMenuService.Current!.Selection;
        switch (control.DataContext)
        {
            case EntityViewModel entity:
                // Right-clicking selects the entity (unless it's already part of a multi-selection), so the
                // menu's verbs act on what the user clicked.
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
