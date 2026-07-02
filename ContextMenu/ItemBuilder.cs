using System;
using System.Threading.Tasks;
using Avalonia.Media;
using Toybox.Studio.Dialogs;
using Toybox.Studio.Utils;

namespace Toybox.Studio.ContextMenu;

/// <summary>
/// The fluent handle for one row being authored on a <see cref="MenuBuilder"/>: its display (icon / colour /
/// shortcut / search keywords), whether it shows (<see cref="VisibleWhen"/>) or greys out
/// (<see cref="EnabledWhen"/>), and the action it runs. Every setter returns <c>this</c> so a row reads as a
/// single chained statement. A <see cref="MenuBuilder.Item"/> call returns one of these; you finish it with a
/// <c>Run</c> overload (or leave it actionless for a static row).
/// </summary>
public sealed class ItemBuilder
{
    private readonly bool _separator;
    private readonly string _label;
    private readonly Icon _icon;
    private Color? _color;
    private string? _gesture;
    private string? _keywords;
    private bool _visible = true;
    private bool _enabled = true;
    private string? _disabledReason;
    private Func<Task>? _run;

    internal ItemBuilder(string label, Icon icon)
    {
        _label = label;
        _icon = icon;
    }

    private ItemBuilder()
    {
        _separator = true;
        _label = "";
        _icon = Icon.None;
    }

    internal static ItemBuilder Separator() => new();

    internal bool IsVisible => _visible;

    /// <summary>Colours the row's icon (e.g. <c>Colors.Red</c>).</summary>
    public ItemBuilder Color(Color color)
    {
        _color = color;
        return this;
    }

    /// <summary>The right-aligned shortcut hint (display only, e.g. <c>"Ctrl+C"</c>).</summary>
    public ItemBuilder Gesture(string hint)
    {
        _gesture = hint;
        return this;
    }

    /// <summary>Extra words the menu's search box matches this row on, beyond its label.</summary>
    public ItemBuilder Keywords(string words)
    {
        _keywords = words;
        return this;
    }

    /// <summary>Shows the row only when <paramref name="condition"/> holds; otherwise it is omitted entirely.</summary>
    public ItemBuilder VisibleWhen(bool condition)
    {
        _visible = condition;
        return this;
    }

    /// <summary>Greys the row out (visible but unclickable) when <paramref name="condition"/> is false.</summary>
    public ItemBuilder EnabledWhen(bool condition)
    {
        _enabled = condition;
        return this;
    }

    /// <summary>The tooltip shown on a greyed-out row explaining why it's disabled (see <see cref="EnabledWhen"/>).</summary>
    public ItemBuilder DisabledReason(string reason)
    {
        _disabledReason = reason;
        return this;
    }

    /// <summary>Runs a synchronous action when the row is chosen.</summary>
    public void Run(Action action) =>
        _run = () =>
        {
            action();
            return Task.CompletedTask;
        };

    /// <summary>Runs an asynchronous action when the row is chosen.</summary>
    public void Run(Func<Task> action) => _run = action;

    /// <summary>Runs a fallible action, surfacing a failure as an error popup titled <paramref name="failureTitle"/>.</summary>
    public void Run(string failureTitle, Func<Task<Result>> action) =>
        _run = () => GuardAsync(failureTitle, action);

    // Materialises the authored row. Visibility is applied by the builder; a disabled row keeps its reason as a
    // tooltip (an enabled row never needs one).
    internal MenuItem Build() =>
        _separator
            ? MenuItem.Separator()
            : new MenuItem
            {
                Label = _label,
                Icon = _icon,
                IconColor = _color,
                Gesture = _gesture,
                Keywords = _keywords,
                IsEnabled = _enabled,
                DisabledReason = _enabled ? null : _disabledReason,
                Run = _run,
            };

    private static async Task GuardAsync(string failureTitle, Func<Task<Result>> action)
    {
        var result = await action().ContinueOnAnyContext();
        if (!result.Success)
            await Popups.ShowErrorAsync(failureTitle, result.Error!).ContinueOnAnyContext();
    }
}
