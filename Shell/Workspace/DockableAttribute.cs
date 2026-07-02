using System;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Shell.Workspace;

/// <summary>
/// Marks a View (a UserControl) as a dockable panel. The <see cref="DockableCatalog"/> reflection-scans
/// the assembly for these at startup and turns each into a <see cref="DockableDescriptor"/>, so a panel
/// is declared in exactly one place — on its own View — and auto-registers into DI, the Windows menu,
/// and the dock. A dockable is identified by its view-model type (the catalog derives the layout-persistence
/// key from it), so there is no id to author. Adding a new dockable is: create the widget, add this attribute.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class DockableAttribute : Attribute
{
    /// <summary>Tab/window title. Falls back to the view-model type's name when unset.</summary>
    public string Title { get; init; } = "";

    /// <summary>Lucide icon shown in the Windows menu and on the panel's tab/title-bar header
    /// (<see cref="Icon.None"/> for none).</summary>
    public Icon Icon { get; init; }

    /// <summary>Palette colour tinting <see cref="Icon"/>, as a strongly-typed <see cref="PaletteColor"/> token
    /// (e.g. <c>PaletteColor.Cyan</c>); <see cref="PaletteColor.None"/> inherits. A C# attribute can't carry a
    /// <c>Color</c> value, so the token names the colour and <c>Colors.ToColor</c> resolves it.</summary>
    public PaletteColor IconColor { get; init; }

    /// <summary>Width of the floating window when the dockable is opened standalone.</summary>
    public double Width { get; init; } = 800;

    /// <summary>Height of the floating window when the dockable is opened standalone.</summary>
    public double Height { get; init; } = 600;

    /// <summary>Where the dockable sits in the default layout (or <see cref="DockSlot.Float"/>).</summary>
    public DockSlot Slot { get; init; } = DockSlot.Float;

    /// <summary>
    /// Proportion of its dock row/column in the default layout. Left/Right set the column width and
    /// CenterTop/CenterBottom set the center row heights; the center column width is the remainder.
    /// </summary>
    public double Proportion { get; init; } = double.NaN;

    /// <summary>
    /// The view-model type backing this dockable. Defaults to the <c>XxxView → XxxViewModel</c>
    /// same-namespace convention; set this when the convention doesn't hold (e.g. a shared view-model).
    /// </summary>
    public Type? ViewModel { get; init; }

    /// <summary>Tie-breaker for ordering within a slot and in the Windows menu (lower comes first).</summary>
    public int Order { get; init; }

    /// <summary>
    /// When <c>true</c> (the default), there is at most one of this dockable: opening it from the
    /// Windows menu focuses the existing one. When <c>false</c>, every open spawns a fresh instance
    /// (its own view-model and engine resources) — used by the viewport, where each window drives a
    /// separate engine camera. Non-singleton view-models are registered transient and disposed when
    /// their window closes.
    /// </summary>
    public bool Singleton { get; init; } = true;
}
