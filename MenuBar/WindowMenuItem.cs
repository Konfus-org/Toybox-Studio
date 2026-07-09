using System.Windows.Input;
using Avalonia.Input;
using Icon = IconPacks.Avalonia.Lucide.PackIconLucideKind;
using KeyGesture = Avalonia.Input.KeyGesture;

namespace Toybox.Studio.MenuBar;

/// <summary>
/// One row of the auto-populated Window menu: a dockable to open (icon + title + open command), or one
/// of the fixed layout actions appended after them. Carries the row's current keybinding as its
/// shortcut hint; rows are rebuilt when the keymap changes. A plain row view-model so the menu's
/// compiled bindings stay statically typed.
/// </summary>
public sealed class WindowMenuItem
{
    public WindowMenuItem(string title, Icon icon, ICommand openCommand, KeyGesture? gesture = null)
    {
        Title = title;
        Icon = icon;
        OpenCommand = openCommand;
        Gesture = gesture;
    }

    public string Title { get; }

    public Icon Icon { get; }

    public ICommand OpenCommand { get; }

    /// <summary>The row's current keybinding, shown as the menu shortcut hint; null while unbound.</summary>
    public KeyGesture? Gesture { get; }
}
