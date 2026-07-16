using Avalonia.Controls;
using Toybox.Studio.Utils.Attributes;

namespace Toybox.Studio.LogConsole;

/// <summary>
/// The Log Console dockable: the unified log stream as a single selectable, tailing text console, pinned to
/// the bottom edge by default. A singleton panel — one console for the workspace. The panel is just the
/// generic <c>ConsoleView</c>; all the log-specific behaviour (severities, source links) lives in
/// <see cref="LogConsoleViewModel"/>.
/// </summary>
[Dockable(Title = "Console", Icon = "Terminal", Slot = DockSlot.Bottom, Proportion = 0.3, Order = 0)]
public partial class LogConsoleView : UserControl
{
    public LogConsoleView()
    {
        InitializeComponent();
    }
}
