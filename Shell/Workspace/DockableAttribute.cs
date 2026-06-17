using System;

namespace Toybox.Studio.Shell.Workspace;

/// <summary>
/// Marks a View (a UserControl) as a dockable panel. The <see cref="DockableCatalog"/> reflection-scans
/// the assembly for these at startup and turns each into a <see cref="DockableDescriptor"/>, so a panel
/// is declared in exactly one place — on its own View — and auto-registers into DI, the Windows menu,
/// and the dock. Adding a new dockable is: create the widget, add this attribute.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class DockableAttribute(string id) : Attribute
{
    /// <summary>Stable identity, persisted in saved layouts and used to focus/dedupe an open dockable.</summary>
    public string Id { get; } = id;

    /// <summary>Tab/window title. Falls back to <see cref="Id"/> when unset.</summary>
    public string Title { get; init; } = "";

    /// <summary>Optional Lucide icon name shown in the Windows menu.</summary>
    public string? Icon { get; init; }

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
