using Toybox.Studio.Keybindings;
using Toybox.Studio.MenuBar;
using Toybox.Studio.Status;
using Toybox.Studio.Workspaces;

namespace Toybox.Studio.Shell;

/// <summary>
/// The main window's content: the menu bar on top, the dockable workspace (whose default center panel
/// is the 3D viewport into the engine's world), and the status bar. The window's key-downs run through
/// the keybinding dispatcher (attached in the window's XAML), so every registered editor action is
/// invokable from the keyboard anywhere in the window. The window's working dock layout is its own: it
/// names the key, points the workspace at it on creation, and saves under it on close.
/// </summary>
public sealed class MainWindowViewModel
{
    // The name the working layout is stored under ("last.json"); restored on the next launch.
    private const string LastLayoutKey = "last";

    public MainWindowViewModel(
        MenuBarViewModel menuBar,
        StatusViewModel status,
        WorkspaceViewModel workspace,
        KeybindingDispatcher keybindings)
    {
        MenuBar = menuBar;
        Status = status;
        Workspace = workspace;
        Keybindings = keybindings;
        Workspace.StartupLayout = LastLayoutKey;
    }

    /// <summary>Persists the working layout under the window's key. Wired to the window's Closing —
    /// before its floating windows close and leave the dock model. The layout snapshot is taken
    /// synchronously; the returned task is the file write still in flight.</summary>
    public Task SaveLayoutAsync() => Workspace.SaveLayoutAsync(LastLayoutKey);

    public string Title => "Toybox Studio";

    public MenuBarViewModel MenuBar { get; }

    public StatusViewModel Status { get; }

    public WorkspaceViewModel Workspace { get; }

    /// <summary>The window's keybinding dispatcher, attached via <see cref="KeybindingScope"/>.</summary>
    public KeybindingDispatcher Keybindings { get; }
}
