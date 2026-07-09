using Avalonia.Media;
using Avalonia.Media.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toybox.Studio.Events;
using Toybox.Studio.Keybindings;

namespace Toybox.Studio.Toolbar;

/// <summary>
/// One toolbar button: a registered editor action's face (icon + tooltip with its live chord hint)
/// whose click publishes the same <see cref="EditorActionInvoked"/> a keybinding or menu would — one
/// invocation path, whatever triggered it. The checked state re-derives from the supplied predicate
/// whenever the owning toolbar's <see cref="ToolbarViewModel"/> refreshes (its domain's changed
/// event — a radio member's "my mode is active", or a toggle's "feature on"), so the row always
/// mirrors the domain no matter who changed it.
/// </summary>
public sealed partial class ToolbarItemViewModel : ObservableEventSubscriber,
    IEventHandler<KeybindingsChanged>
{
    private readonly string _actionId;
    private readonly ActionRegistry _actions;
    private readonly EditorKeymap _keymap;
    private readonly Func<bool> _isActive;
    private readonly string _title;

    public ToolbarItemViewModel(
        string actionId,
        Color iconColor,
        Func<bool> isActive,
        ActionRegistry actions,
        EditorKeymap keymap,
        EventDispatcher events)
        : base(events)
    {
        _actionId = actionId;
        _actions = actions;
        _keymap = keymap;
        _isActive = isActive;
        _colorBrush = new ImmutableSolidColorBrush(iconColor);

        var action = actions.Find(actionId);
        _title = action?.Title ?? actionId;
        IconKind = action?.Icon ?? Icon.None;
        Tooltip = BuildTooltip();
        IsActive = isActive();
    }

    // The glyph flips to white when active; at rest it wears its own tool color.
    private static readonly IBrush ActiveGlyphBrush = new ImmutableSolidColorBrush(Colors.White);
    private readonly IBrush _colorBrush;

    /// <summary>The action's Lucide glyph.</summary>
    public Icon IconKind { get; }

    /// <summary>The glyph color: white when active (it sits on the color fill), else the tool's own
    /// color so the row reads at a glance.</summary>
    public IBrush IconBrush => IsActive ? ActiveGlyphBrush : _colorBrush;

    /// <summary>The button fill: the tool's color when active (the icon inverts to white on it),
    /// transparent otherwise so the toolbar's scrim shows through.</summary>
    public IBrush BackgroundBrush => IsActive ? _colorBrush : Brushes.Transparent;

    /// <summary>The action title plus its current chord hint ("Move Tool (W)").</summary>
    [ObservableProperty]
    public partial string Tooltip { get; private set; }

    /// <summary>Whether the button reads checked (fills with the tool color, white glyph).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IconBrush))]
    [NotifyPropertyChangedFor(nameof(BackgroundBrush))]
    public partial bool IsActive { get; private set; }

    /// <summary>Re-derives the checked state from the predicate (the owning toolbar calls this on
    /// its domain's changed event).</summary>
    public void Refresh() => IsActive = _isActive();

    [RelayCommand]
    private void Run() => _actions.Invoke(_actionId);

    public void Handle(in KeybindingsChanged evt) => Tooltip = BuildTooltip();

    private string BuildTooltip() =>
        _keymap.GestureFor(_actionId) is { } gesture ? $"{_title} ({gesture})" : _title;
}
