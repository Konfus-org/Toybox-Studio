using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Toybox.Studio.Utils;

namespace Toybox.Studio.ContextMenu;

/// <summary>
/// Opens the right context menu on right-click, routed purely by type. Set <c>cm:MenuOpenBehavior.Enabled="True"</c>
/// on any control and a right-click (or the context-menu key) builds the menu registered for that control's
/// <c>DataContext</c> — an <c>EntityViewModel</c> row gets the entity menu, a <c>PropertyViewModel</c> row the
/// property menu, a panel's empty space the panel view-model's menu, and so on — and shows it in a flyout at the
/// pointer. No host string: the <see cref="ContextMenuCatalog"/> matches the data context's type. The handler is
/// on the <b>tunnel</b> route so it fires before the event reaches a descendant editor (a <c>TextBox</c> in a
/// property row), claiming the gesture so the editor's native text menu never preempts the menu.
/// </summary>
public static class MenuOpenBehavior
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Enabled", typeof(MenuOpenBehavior));

    static MenuOpenBehavior() => EnabledProperty.Changed.AddClassHandler<Control>(OnEnabledChanged);

    public static void SetEnabled(Control control, bool value) => control.SetValue(EnabledProperty, value);

    public static bool GetEnabled(Control control) => control.GetValue(EnabledProperty);

    /// <summary>
    /// Opens the world menu anchored to <paramref name="anchor"/> at the pointer. Used by the viewport, which
    /// picks + selects the entity under the cursor first (<paramref name="hitEntity"/> set) then shows that
    /// entity's verbs; a miss (null) shows the same "add / paste" rows as the tree's empty space. Either way it's
    /// the viewport surface, so the tree-only verbs stay hidden.
    /// </summary>
    public static Task ShowWorldMenuAsync(Control anchor, ulong? hitEntity)
    {
        if (ContextMenuCatalog.Current is not { } catalog)
            return Task.CompletedTask;

        return BuildAndShowAsync(anchor, catalog.BuildAsync(new ViewportMenuTarget(hitEntity)));
    }

    private static void OnEnabledChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        control.RemoveHandler(InputElement.ContextRequestedEvent, OnContextRequested);
        if (e.NewValue is true)
            control.AddHandler(
                InputElement.ContextRequestedEvent, OnContextRequested,
                RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private static void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not Control { DataContext: { } dataContext }
            || ContextMenuCatalog.Current is not { } catalog
            || !catalog.Handles(dataContext))
            return;

        // Claim the gesture before the async build so a descendant editor's native menu — or an ancestor's
        // menu — never also opens. Tunnelling means this runs before the event reaches the editor.
        e.Handled = true;
        BuildAndShowAsync((Control)sender, catalog.BuildAsync(dataContext)).FireAndForget();
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
}
