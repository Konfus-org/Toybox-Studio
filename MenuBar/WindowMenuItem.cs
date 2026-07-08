using System.Windows.Input;
using Icon = IconPacks.Avalonia.Lucide.PackIconLucideKind;

namespace Toybox.Studio.MenuBar;

/// <summary>
/// One row of the auto-populated Window menu: a dockable to open (icon + title + open command), or one
/// of the fixed layout actions appended after them. A plain row view-model so the menu's compiled
/// bindings stay statically typed.
/// </summary>
public sealed class WindowMenuItem
{
    public WindowMenuItem(string title, Icon icon, ICommand openCommand)
    {
        Title = title;
        Icon = icon;
        OpenCommand = openCommand;
    }

    public string Title { get; }

    public Icon Icon { get; }

    public ICommand OpenCommand { get; }
}
