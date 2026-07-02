using Avalonia.Media;
using Dock.Model.Avalonia.Controls;
using Newtonsoft.Json;

namespace Toybox.Studio.Shell.Workspace;

/// <summary>
/// A dock <see cref="Tool"/> that carries its dockable's header icon (a Lucide icon name + optional tbx colour
/// constant). Dock's model has no icon concept, so the window manager stamps these from the
/// <see cref="DockableDescriptor"/> when a tool is created or a saved layout is rehydrated, and the tab strip /
/// chrome title-bar templates render them through <see cref="WorkspaceToolIconConverter"/>. The icon is derived
/// from the descriptor every time, so it is <see cref="JsonIgnoreAttribute">not persisted</see> with the layout.
/// <see cref="ToolbarTool"/> and <see cref="AssetViewerTool"/> derive from this so every dockable's tool — plain,
/// toolbar-hosting, or asset-previewing — gets a header icon.
/// </summary>
public class WorkspaceTool : Tool
{
    /// <summary>The Lucide icon shown on the tab and chrome header (<see cref="Icon.None"/> hides the glyph).</summary>
    [JsonIgnore]
    public Icon IconName { get; set; }

    /// <summary>The palette colour tinting the header icon (null to inherit).</summary>
    [JsonIgnore]
    public Color? IconColor { get; set; }
}
