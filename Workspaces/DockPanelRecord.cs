using Dock.Model.Avalonia.Controls;
using Newtonsoft.Json;

namespace Toybox.Studio.Workspaces;

/// <summary>
/// The docking system's record of an open panel: the dock <see cref="Tool"/> the workspace tabs, drags,
/// and persists in layouts, distinct from the panel view/view-model rendered as its content. It also
/// carries the dockable's header icon — Dock's model has no icon concept, so the factory stamps it from
/// the <see cref="DockableDescriptor"/> when a record is created or a saved layout is rehydrated, and
/// the tab strip / chrome title-bar templates render it through
/// <see cref="DockPanelRecordIconConverter"/>. The icon is derived from the descriptor every time, so it
/// is <see cref="JsonIgnoreAttribute">not persisted</see> with the layout.
/// </summary>
public class DockPanelRecord : Tool
{
    /// <summary>The Lucide icon shown on the tab and chrome header (<see cref="Icon.None"/> hides the glyph).</summary>
    [JsonIgnore]
    public Icon IconName { get; set; }
}
