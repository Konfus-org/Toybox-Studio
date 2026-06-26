using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Toybox.Studio.Services.Clipboard;
using Toybox.Studio.Services.Commands;
using Toybox.Studio.Services.Favorites;
using Toybox.Studio.Services.World;
using Toybox.Studio.Utils;
using Toybox.Studio.Widgets.PropertyGrid;
using Toybox.Studio.Widgets.Toolbar;

namespace Toybox.Studio.Widgets.ContextMenu;

/// <summary>
/// The view-layer entry point for the data-driven context menus: resolves a menu by id and builds the
/// <see cref="SearchableMenuViewModel"/> the flyout binds to, wiring in the shared command runner, favorites
/// store, selection and clipboard. Also builds the per-row property menu (copy/paste a value, reset to default),
/// whose actions are local to a <see cref="PropertyViewModel"/> rather than data-driven engine verbs, yet reuse
/// the same searchable, favoritable surface. Like <c>ScriptEditing.Current</c>, a single instance is published
/// to <see cref="Current"/> at startup so the static <see cref="MenuOpenBehavior"/> can reach these services
/// without a per-view hookup.
/// </summary>
public sealed class ContextMenuService
{
    private readonly MenuCatalog _catalog;
    private readonly ToolCommandRunner _runner;
    private readonly FavoritesManager _favorites;
    private readonly Clipboard _clipboard;

    public ContextMenuService(
        MenuCatalog catalog,
        ToolCommandRunner runner,
        FavoritesManager favorites,
        WorldSelection selection,
        Clipboard clipboard)
    {
        _catalog = catalog;
        _runner = runner;
        _favorites = favorites;
        _clipboard = clipboard;
        Selection = selection;
    }

    /// <summary>The app-wide instance, set once at startup (see <c>App.StartupAsync</c>).</summary>
    public static ContextMenuService? Current { get; set; }

    /// <summary>The shared entity selection — the menu-open gesture selects the right-clicked entity first.</summary>
    public WorldSelection Selection { get; }

    /// <summary>
    /// Builds the data-driven menu for <paramref name="menuId"/> over <paramref name="context"/>, or null when
    /// no such menu exists or every item is filtered out. The caller hosts the returned view-model in a flyout
    /// and disposes it on close.
    /// </summary>
    public SearchableMenuViewModel? Build(string menuId, MenuContext context)
    {
        if (_catalog.Resolve(menuId) is not { } definition)
            return null;

        var count = Selection.SelectedIds.Count;
        var menu = new SearchableMenuViewModel(
            close => definition.Items
                .Where(entry => entry.IsSeparator || Shows(entry.When, context, count))
                .Select(entry => new MenuEntryViewModel(entry, definition.Id, _runner, _favorites, context, close))
                .ToList(),
            _favorites);
        return menu.IsEmpty ? null : menu;
    }

    /// <summary>
    /// Builds the per-row property menu for <paramref name="property"/> — copy/paste its value and reset it to
    /// default, filtered to what the row actually supports — or null when the row offers none of them. The
    /// actions run locally against the view-model (the property grid also drives non-entity data such as
    /// settings, so these can't be global engine verbs).
    /// </summary>
    public SearchableMenuViewModel? BuildProperty(PropertyViewModel property)
    {
        if (_catalog.Resolve(MenuCatalogDefaults.PropertyMenu) is not { } definition)
            return null;

        var hasValue = property.CurrentValue is not null;

        bool Applies(MenuEntry entry) => entry.Id switch
        {
            "property.copyValue" => hasValue,
            "property.pasteValue" => hasValue,
            "property.reset" => property.CanReset,
            _ => true, // separators
        };

        if (!definition.Items.Any(entry => !entry.IsSeparator && Applies(entry)))
            return null;

        Func<Task> Action(MenuEntry entry) => entry.Id switch
        {
            "property.copyValue" => () => CopyValueAsync(property),
            "property.pasteValue" => () => PasteValueAsync(property),
            "property.reset" => () => RunSync(() => property.ResetToDefault?.Invoke()),
            _ => () => Task.CompletedTask,
        };

        var menu = new SearchableMenuViewModel(
            close => definition.Items
                .Where(entry => entry.IsSeparator || Applies(entry))
                .Select(entry => new MenuEntryViewModel(entry, definition.Id, _favorites, Action(entry), close))
                .ToList(),
            _favorites);
        return menu.IsEmpty ? null : menu;
    }

    private Task CopyValueAsync(PropertyViewModel property) =>
        property.CurrentValue is { } value
            ? _clipboard.PushAsync(new ClipboardPropertyValue { Value = value })
            : Task.CompletedTask;

    private async Task PasteValueAsync(PropertyViewModel property)
    {
        var copy = await _clipboard.PeekAsync<ClipboardPropertyValue>().ContinueOnAnyContext();
        if (copy is not null)
            // ApplyValue mutates the bound token and re-commits — it must run on the UI thread (the clipboard
            // await may resume off it).
            Dispatch.To(DispatchContext.UI, () => property.ApplyValue(copy.Value));
    }

    private static Task RunSync(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    // Whether a data-driven entry is shown for what the menu was opened over.
    private static bool Shows(MenuCondition when, MenuContext context, int selectedCount) => when switch
    {
        MenuCondition.Always => true,
        MenuCondition.Entity => !context.IsBackground && context.EntityId is not null,
        MenuCondition.SingleEntity => !context.IsBackground && selectedCount <= 1 && context.EntityId is not null,
        MenuCondition.MultiEntity => selectedCount > 1,
        MenuCondition.Component => context.Component is not null,
        MenuCondition.Background => context.IsBackground,
        _ => true,
    };
}
