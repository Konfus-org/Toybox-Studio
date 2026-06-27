using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Toybox.Studio.Services.Clipboard;
using Toybox.Studio.Services.Commands;
using Toybox.Studio.Services.Favorites;
using Toybox.Studio.Services.World;
using Toybox.Studio.Utils;
using Toybox.Studio.Widgets.PropertyGrid;

namespace Toybox.Studio.Widgets.ContextMenu;

/// <summary>
/// Builds the editor's context menus in code (no data/JSON layer): each menu is assembled fresh for what was
/// clicked, with arbitrary predicates deciding which rows make sense (single vs multi selection, an entity's
/// global/streamed state, whether the clipboard holds a pasteable item, …). The rows are wrapped in the shared
/// searchable, favoritable <see cref="SearchableMenuViewModel"/>; actions bind straight to
/// <see cref="EditorCommands"/> (entity/component) or to the property row's own view-model. Like
/// <c>ScriptEditing.Current</c>, one instance is published to <see cref="Current"/> at startup so the static
/// <see cref="MenuOpenBehavior"/> can reach it without a per-view hookup.
/// </summary>
public sealed class ContextMenuService
{
    private readonly EditorCommands _editor;
    private readonly Clipboard _clipboard;
    private readonly FavoritesManager _favorites;

    public ContextMenuService(
        EditorCommands editor, WorldSelection selection, Clipboard clipboard, FavoritesManager favorites)
    {
        _editor = editor;
        _clipboard = clipboard;
        _favorites = favorites;
        Selection = selection;
    }

    /// <summary>The app-wide instance, set once at startup (see <c>App.StartupAsync</c>).</summary>
    public static ContextMenuService? Current { get; set; }

    /// <summary>The shared entity selection — the menu-open gesture selects the right-clicked entity first.</summary>
    public WorldSelection Selection { get; }

    /// <summary>
    /// The menu for the current entity selection (delete, move, duplicate, clipboard, global/streamed, …),
    /// pruned to what makes sense for it, or null when nothing is selected.
    /// </summary>
    public async Task<SearchableMenuViewModel?> BuildEntityMenuAsync()
    {
        if (Selection.PrimaryId is not { } primary)
            return null;

        var single = Selection.SelectedIds.Count == 1;
        var canPaste = await _clipboard.Has<ClipboardEntity>().ContinueOnAnyContext();

        var items = new List<MenuItem>();
        if (single)
            items.Add(Item("entity.rename", "Rename", "Pencil", () => Sync(_editor.Rename), gesture: "F2"));
        items.Add(MenuItem.Separator());
        items.Add(Item("entity.cut", "Cut", "Scissors", _editor.CutEntityAsync, gesture: "Ctrl+X"));
        items.Add(Item("entity.copy", "Copy", "Copy", _editor.CopyEntityAsync, gesture: "Ctrl+C"));
        if (canPaste)
            items.Add(Item("entity.paste", "Paste", "ClipboardPaste", _editor.PasteEntityAsync, gesture: "Ctrl+V"));
        items.Add(Item("entity.duplicate", "Duplicate", "CopyPlus", _editor.DuplicateAsync, gesture: "Ctrl+D"));
        items.Add(MenuItem.Separator());
        if (single && _editor.CanMoveUp(primary))
            items.Add(Item("entity.moveUp", "Move Up", "ArrowUp", () => _editor.MoveAsync(up: true)));
        if (single && _editor.CanMoveDown(primary))
            items.Add(Item("entity.moveDown", "Move Down", "ArrowDown", () => _editor.MoveAsync(up: false)));
        items.Add(MenuItem.Separator());
        if (_editor.SelectionHasStreamed())
            items.Add(Item("entity.makeGlobal", "Make Global", "Globe", () => _editor.SetGlobalAsync(true)));
        if (_editor.SelectionHasGlobal())
            items.Add(Item("entity.makeStreamed", "Make Streamed", "Layers", () => _editor.SetGlobalAsync(false)));
        if (single)
            items.Add(Item("entity.toggleEnabled", "Enable / Disable", "Power", _editor.ToggleEnabledAsync));
        items.Add(MenuItem.Separator());
        items.Add(Item("entity.delete", "Delete", "Trash2", _editor.DeleteAsync, iconColor: "RED", gesture: "Del"));

        return Compose(MenuHosts.Entity, items);
    }

    /// <summary>The empty-space (background) menu: add an entity, or paste one when the clipboard holds one.</summary>
    public async Task<SearchableMenuViewModel?> BuildBackgroundMenuAsync()
    {
        var canPaste = await _clipboard.Has<ClipboardEntity>().ContinueOnAnyContext();

        var items = new List<MenuItem>
        {
            Item("background.addEntity", "Add Entity", "Plus", () => _editor.AddAsync(global: false), iconColor: "GREEN"),
            Item("background.addGlobal", "Add Global Entity", "Globe", () => _editor.AddAsync(global: true), iconColor: "GREEN"),
        };
        if (canPaste)
        {
            items.Add(MenuItem.Separator());
            items.Add(Item("background.paste", "Paste", "ClipboardPaste", _editor.PasteEntityAsync, gesture: "Ctrl+V"));
        }

        return Compose(MenuHosts.Background, items);
    }

    /// <summary>The inspector component-header menu: copy/remove the component, and paste one when held.</summary>
    public async Task<SearchableMenuViewModel?> BuildComponentMenuAsync(MenuContext context)
    {
        var canPaste = await _clipboard.Has<ClipboardComponent>().ContinueOnAnyContext();

        var items = new List<MenuItem>
        {
            Item("component.copy", "Copy Component", "Copy", () => _editor.CopyComponentAsync(context)),
        };
        if (canPaste)
            items.Add(Item("component.paste", "Paste Component", "ClipboardPaste",
                () => _editor.PasteComponentAsync(context)));
        items.Add(MenuItem.Separator());
        items.Add(Item("component.remove", "Remove Component", "Trash2",
            () => _editor.RemoveComponentAsync(context), iconColor: "RED"));

        return Compose(MenuHosts.Component, items);
    }

    /// <summary>
    /// The per-row property menu — copy/paste its value and reset it to default, filtered to what the row
    /// supports. Paste appears only when the clipboard holds a value of the same property type (so a Vector3
    /// only pastes into a Vector3). The actions run locally against the row's view-model.
    /// </summary>
    public async Task<SearchableMenuViewModel?> BuildPropertyAsync(PropertyViewModel property)
    {
        var hasValue = property.CurrentValue is not null;
        var canPaste = hasValue
            && await _clipboard.Has<JToken>(variant: property.Type).ContinueOnAnyContext();

        var items = new List<MenuItem>();
        if (hasValue)
            items.Add(Item("property.copyValue", "Copy Value", "Copy", () => CopyValueAsync(property), gesture: "Ctrl+C"));
        if (canPaste)
            items.Add(Item("property.pasteValue", "Paste Value", "ClipboardPaste",
                () => PasteValueAsync(property), gesture: "Ctrl+V"));
        if (property.CanReset)
        {
            items.Add(MenuItem.Separator());
            items.Add(Item("property.reset", "Reset to Default", "RotateCcw",
                () => Sync(() => property.ResetToDefault?.Invoke()), iconColor: "YELLOW"));
        }

        // A list element gets reorder/duplicate/delete on top of its value copy/paste.
        if (property.OwningList is { } list)
            AddListItemActions(items, list, property);

        // The list's own row gets append + paste-as-new-element.
        if (property is ArrayPropertyViewModel { IsResizable: true } arrayList)
            await AddListActionsAsync(items, arrayList).ContinueOnAnyContext();

        return Compose(MenuHosts.Property, items);
    }

    // Reorder / duplicate / delete for one element of a resizable list. Move up/down appear only when there's
    // somewhere to move (so the first row has no "Move Up", the last no "Move Down").
    private static void AddListItemActions(
        List<MenuItem> items, ArrayPropertyViewModel list, PropertyViewModel item)
    {
        items.Add(MenuItem.Separator());
        items.Add(Item("listItem.duplicate", "Duplicate Item", "CopyPlus", () => Sync(() => list.Duplicate(item))));
        if (list.CanMoveUp(item))
            items.Add(Item("listItem.moveUp", "Move Up", "ArrowUp", () => Sync(() => list.MoveUp(item))));
        if (list.CanMoveDown(item))
            items.Add(Item("listItem.moveDown", "Move Down", "ArrowDown", () => Sync(() => list.MoveDown(item))));
        items.Add(Item("listItem.delete", "Delete Item", "Trash2",
            () => Sync(() => list.RemoveItem(item)), iconColor: "RED"));
    }

    // Append / paste-an-element for a resizable list's own row. Paste appears only when the clipboard holds a
    // value of the list's element type (so an element copied from one list pastes as a new entry in another).
    private async Task AddListActionsAsync(List<MenuItem> items, ArrayPropertyViewModel list)
    {
        items.Add(MenuItem.Separator());
        items.Add(Item("list.add", "Add Item", "Plus",
            () => Sync(() => list.AddCommand.Execute(null)), iconColor: "GREEN"));
        if (await _clipboard.Has<JToken>(variant: list.ElementType).ContinueOnAnyContext())
            items.Add(Item("list.pasteItem", "Paste Item", "ClipboardPaste", () => PasteItemAsync(list)));
    }

    private async Task PasteItemAsync(ArrayPropertyViewModel list)
    {
        var value = await _clipboard.Paste<JToken>(variant: list.ElementType).ContinueOnAnyContext();
        if (value is not null)
            // AppendValue mutates the backing array and re-commits — it must run on the UI thread (the clipboard
            // await may resume off it).
            Dispatch.To(DispatchContext.UI, () => list.AppendValue(value));
    }

    // A property value is just JSON; the property's type token narrows the clipboard kind so a Vector3 only
    // pastes into a Vector3, a Color into a Color, and so on — no per-property clipboard type needed.
    private Task CopyValueAsync(PropertyViewModel property) =>
        property.CurrentValue is { } value
            ? _clipboard.Copy(value, variant: property.Type)
            : Task.CompletedTask;

    private async Task PasteValueAsync(PropertyViewModel property)
    {
        var value = await _clipboard.Paste<JToken>(variant: property.Type).ContinueOnAnyContext();
        if (value is not null)
            // ApplyValue mutates the bound token and re-commits — it must run on the UI thread (the clipboard
            // await may resume off it).
            Dispatch.To(DispatchContext.UI, () => property.ApplyValue(value));
    }

    // Wraps a built item list in the shared searchable/favoritable surface; null when there's nothing to show.
    private SearchableMenuViewModel? Compose(string host, IReadOnlyList<MenuItem> items)
    {
        if (!items.Any(item => !item.IsSeparator))
            return null;

        var menu = new SearchableMenuViewModel(
            close => items.Select(item => new MenuEntryViewModel(item, host, _favorites, close)).ToList(),
            _favorites);
        return menu.IsEmpty ? null : menu;
    }

    private static MenuItem Item(
        string id, string label, string icon, Func<Task> run, string? iconColor = null, string? gesture = null) =>
        new() { Id = id, Label = label, Icon = icon, IconColor = iconColor, Gesture = gesture, Run = run };

    // Adapts a synchronous editor action to the Func<Task> a menu row expects.
    private static Task Sync(Action action)
    {
        action();
        return Task.CompletedTask;
    }
}
