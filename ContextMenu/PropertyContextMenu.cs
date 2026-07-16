using Newtonsoft.Json.Linq;
using System.Threading.Tasks;
using Toybox.Studio.Clipboards;
using Toybox.Studio.Events;
using Toybox.Studio.Favorites;
using Toybox.Studio.PropertyGrid.Slots;
using Toybox.Studio.PropertyGrid;
using Toybox.Studio.Utils;

namespace Toybox.Studio.ContextMenu;

/// <summary>
/// The per-row property menu: copy/paste the value and reset it to default, plus reorder/delete for a resizable
/// list's element and append for the list's own row. Routed for the clicked <see cref="PropertyNode"/>; the only
/// service it holds is the clipboard (a value travels as JSON, narrowed by the row's CLR type so a Vector3 only
/// pastes into a Vector3). Value copy/paste/reset reach the row's value slot (<see cref="ValueViewModel"/>); the
/// list actions reach the element's owning <see cref="ListPropertyNode"/>.
/// </summary>
public sealed class PropertyContextMenu : ContextMenu<PropertyNode>
{
    private readonly Clipboard _clipboard;

    public PropertyContextMenu(FavoritesManager favorites, EventDispatcher events, Clipboard clipboard)
        : base(favorites, events) => _clipboard = clipboard;

    protected override async Task Build(MenuBuilder menu, PropertyNode target)
    {
        if (target.Value is ValueViewModel value)
            await BuildValueActionsAsync(menu, value).ContinueOnAnyContext();

        // A list element gets reorder/delete on top of its value copy/paste.
        if (target.OwningList is { } list)
            AddElementActions(menu, list, target);

        // The list's own header row gets append.
        if (target is ListPropertyNode listNode)
        {
            menu.Separator();
            menu.Item("Add Item", Icon.Plus).Color(Palette.Green).Run(listNode.AddNew);
        }
    }

    private async Task BuildValueActionsAsync(MenuBuilder menu, ValueViewModel value)
    {
        var hasValue = value.CurrentValue is not null && value.ValueType is not null;
        var canPaste = value.ValueType is { } type
            && !value.IsReadOnly
            && await _clipboard.Has<JToken>(variant: type.Name).ContinueOnAnyContext();

        menu.Item("Copy Value", Icon.Copy).Gesture("Ctrl+C")
            .VisibleWhen(hasValue).Run(() => CopyValueAsync(value));
        menu.Item("Paste Value", Icon.ClipboardPaste).Gesture("Ctrl+V")
            .VisibleWhen(canPaste).Run(() => PasteValueAsync(value));
        menu.Item("Reset to Default", Icon.RotateCcw).Color(Palette.Yellow)
            .VisibleWhen(value.CanReset).Run(value.ResetToDefault);
    }

    // Reorder / delete for one element of a resizable list. Move up/down appear only when there's somewhere to
    // move, so the first row has no "Move Up" and the last no "Move Down".
    private static void AddElementActions(MenuBuilder menu, ListPropertyNode list, PropertyNode element)
    {
        var index = list.IndexOf(element);
        var last = list.Children.Count - 1;

        menu.Separator();
        menu.Item("Move Up", Icon.ArrowUp)
            .VisibleWhen(index > 0).Run(() => list.Move(index, index - 1));
        menu.Item("Move Down", Icon.ArrowDown)
            .VisibleWhen(index >= 0 && index < last).Run(() => list.Move(index, index + 1));
        menu.Item("Delete Item", Icon.Trash2).Color(Palette.Red).Run(() => list.Remove(element));
    }

    // A property value is just JSON; the value's CLR type narrows the clipboard kind so a Vector3 only pastes
    // into a Vector3, a Color into a Color, and so on.
    private Task CopyValueAsync(ValueViewModel value) =>
        value.CurrentValue is { } current && value.ValueType is { } type
            ? _clipboard.Copy(JToken.FromObject(current), variant: type.Name)
            : Task.CompletedTask;

    private async Task PasteValueAsync(ValueViewModel value)
    {
        if (value.ValueType is not { } type)
            return;

        var token = await _clipboard.Paste<JToken>(variant: type.Name).ContinueOnAnyContext();
        if (token is null)
            return;

        // Materialise the token back into the row's own type, then write on the UI thread (the clipboard await
        // may resume off it, and ApplyValue mutates the bound value).
        var typed = token.ToObject(type);
        Dispatch.To(DispatchContext.UI, () => value.ApplyValue(typed));
    }
}
