using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Toybox.Studio.Widgets.Viewport;

/// <summary>
/// One entity's billboard in the editor viewport overlay: a name label plus a stack of component icons
/// (<see cref="Icons"/>), drawn at the entity's projected screen position and clickable to select it. The
/// engine streams the normalized position each frame (<see cref="U"/>/<see cref="V"/>/<see cref="Depth"/>);
/// the owning <see cref="ViewportViewModel"/> turns that into the control-space <see cref="X"/>/<see cref="Y"/>
/// the overlay binds to. Name + icons come from the world snapshot and rarely change, so they are fixed here.
/// </summary>
public sealed partial class BillboardViewModel(ulong id, string name, IReadOnlyList<BillboardIcon> icons)
    : ObservableObject
{
    public ulong Id { get; } = id;

    public string Name { get; } = name;

    public IReadOnlyList<BillboardIcon> Icons { get; } = icons;

    public bool HasIcons => Icons.Count > 0;

    /// <summary>Normalized image coordinates (top-left origin) of the entity this frame; the view-model
    /// reprojects these into <see cref="X"/>/<see cref="Y"/> for the current overlay size.</summary>
    public double U { get; set; }

    public double V { get; set; }

    /// <summary>World-space distance from the camera, for optional distance fade/scaling.</summary>
    public double Depth { get; set; }

    /// <summary>Control-space position (pixels) the overlay places this billboard at.</summary>
    [ObservableProperty]
    public partial double X { get; set; }

    [ObservableProperty]
    public partial double Y { get; set; }

    /// <summary>Whether this entity is in the current selection (drives the overlay highlight).</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>Whether the entity is hidden behind scene geometry from the camera; occluded icons are not
    /// shown. Refreshed periodically via the engine's occlusion query.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Visible))]
    public partial bool IsOccluded { get; set; }

    /// <summary>Whether the icon should be drawn (it has somewhere to be and isn't occluded).</summary>
    public bool Visible => !IsOccluded;
}
