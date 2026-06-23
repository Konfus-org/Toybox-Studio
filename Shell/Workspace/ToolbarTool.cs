using Dock.Model.Avalonia.Controls;
using Toybox.Studio.Widgets.Toolbar;

namespace Toybox.Studio.Shell.Workspace;

/// <summary>
/// A dock <see cref="Tool"/> that carries a per-instance <see cref="ToolbarLayout"/>. Because the layout
/// serializer round-trips the whole tool tree (type-tagged, stripping only live content), this state rides
/// along with the saved layout — so each viewport remembers its own toolbar (docked edge + ordered tools)
/// across restarts. The window manager creates one of these for any dockable whose view-model is an
/// <see cref="IToolbarHost"/>.
/// </summary>
public sealed class ToolbarTool : Tool
{
    public ToolbarLayout Toolbar { get; set; } = ToolbarLayout.Default();
}
