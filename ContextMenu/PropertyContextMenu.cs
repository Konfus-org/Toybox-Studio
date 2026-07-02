using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Toybox.Studio.Clipboards;
using Toybox.Studio.Favorites;
using Toybox.Studio.Utils;
using Toybox.Studio.PropertyGrid;

namespace Toybox.Studio.ContextMenu;

/// <summary>
/// The per-row property menu: copy/paste the value and reset it to default, plus list reorder/duplicate/delete
/// and append/paste-element for a resizable list row. Routed for the clicked <see cref="PropertyViewModel"/>; the
/// only service it holds is the clipboard (a value is just typed JSON, narrowed by the row's type token so a
/// Vector3 only pastes into a Vector3).
/// </summary>
public sealed class PropertyContextMenu : ContextMenu<PropertyViewModel>
{
    private readonly Clipboard _clipboard;

    public PropertyContextMenu(FavoritesManager favorites, Clipboard clipboard) : base(favorites) =>
        _clipboard = clipboard;

    protected override async Task Build(MenuBuilder menu, PropertyViewModel target)
    {
        var hasValue = target.CurrentValue is not null;
        var canPaste = hasValue
            && await _clipboard.Has<JToken>(variant: target.Type).ContinueOnAnyContext();

        menu.Item("Copy Value", Icon.Copy).Gesture("Ctrl+C")
            .VisibleWhen(hasValue).Run(() => CopyValueAsync(target));
        menu.Item("Paste Value", Icon.ClipboardPaste).Gesture("Ctrl+V")
            .VisibleWhen(canPaste).Run(() => PasteValueAsync(target));
        if (target.CanReset)
        {
            menu.Separator();
            menu.Item("Reset to Default", Icon.RotateCcw).Color(Utils.Colors.Yellow)
                .Run(() => target.ResetToDefault?.Invoke());
        }

        // A list element gets reorder/duplicate/delete on top of its value copy/paste.
        if (target.OwningList is { } list)
            AddListItemActions(menu, list, target);

        // The list's own row gets append + paste-as-new-element.
        if (target is ArrayPropertyViewModel { IsResizable: true } arrayList)
            await AddListActionsAsync(menu, arrayList).ContinueOnAnyContext();
    }

    // Reorder / duplicate / delete for one element of a resizable list. Move up/down appear only when there's
    // somewhere to move (so the first row has no "Move Up", the last no "Move Down").
    private static void AddListItemActions(MenuBuilder menu, ArrayPropertyViewModel list, PropertyViewModel item)
    {
        menu.Separator();
        menu.Item("Duplicate Item", Icon.CopyPlus).Run(() => list.Duplicate(item));
        menu.Item("Move Up", Icon.ArrowUp)
            .VisibleWhen(list.CanMoveUp(item)).Run(() => list.MoveUp(item));
        menu.Item("Move Down", Icon.ArrowDown)
            .VisibleWhen(list.CanMoveDown(item)).Run(() => list.MoveDown(item));
        menu.Item("Delete Item", Icon.Trash2).Color(Utils.Colors.Red).Run(() => list.RemoveItem(item));
    }

    // Append / paste-an-element for a resizable list's own row. Paste appears only when the clipboard holds a
    // value of the list's element type (so an element copied from one list pastes as a new entry in another).
    private async Task AddListActionsAsync(MenuBuilder menu, ArrayPropertyViewModel list)
    {
        var canPaste = await _clipboard.Has<JToken>(variant: list.ElementType).ContinueOnAnyContext();
        menu.Separator();
        menu.Item("Add Item", Icon.Plus).Color(Utils.Colors.Green)
            .Run(() => list.AddCommand.Execute(null));
        menu.Item("Paste Item", Icon.ClipboardPaste)
            .VisibleWhen(canPaste).Run(() => PasteItemAsync(list));
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
}
