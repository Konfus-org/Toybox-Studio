namespace Toybox.Studio.Utils.Attributes;

/// <summary>
/// Marks a View (a UserControl) as a dockable panel. The workspace's dockable catalog reflection-scans
/// the assemblies for these and turns each into a registered panel, so a dockable is declared in
/// exactly one place — on its own View — with no id to author: it is identified by its view-model type
/// (the <c>XxxView → XxxViewModel</c> same-namespace convention, or an explicit
/// <see cref="ViewModel"/>). Lives in Utils so feature projects can declare their panels without
/// referencing the shell.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class DockableAttribute : Attribute
{
    /// <summary>Tab/window title. Falls back to the view-model type's name when unset.</summary>
    public string Title { get; init; } = "";

    /// <summary>The Lucide icon shown on the panel's tab/header and in the Windows menu, by the
    /// icon-pack kind's member name (as <see cref="IconAttribute"/>); empty for none.</summary>
    public string Icon { get; init; } = "";

    /// <summary>
    /// The view-model type backing this dockable. Defaults to the <c>XxxView → XxxViewModel</c>
    /// same-namespace convention; set this when the convention doesn't hold (e.g. a shared view-model).
    /// </summary>
    public Type? ViewModel { get; init; }

    /// <summary>The edge this dockable docks to in the default layout (or <see cref="DockSlot.Float"/>
    /// to stay out of it) — see <see cref="DockSlot"/> for the edge semantics.</summary>
    public DockSlot Slot { get; init; } = DockSlot.Float;

    /// <summary>
    /// The dockable this one docks relative to, by its view-model type (the dockable identity). Null —
    /// the default — anchors <see cref="Slot"/> to the main window; set, this dockable's dock splits
    /// off the dock containing the parent on the <see cref="Slot"/> edge (e.g. an asset browser docked
    /// <see cref="DockSlot.Bottom"/> of the viewport). Meaningless for <see cref="DockSlot.Float"/>.
    /// </summary>
    public Type? Parent { get; init; }

    /// <summary>
    /// Proportion of its dock row/column in the default layout. Left/Right set the column width,
    /// Top/Bottom the center-column row heights, and a parented dockable the share it splits off its
    /// parent's dock; the remainder goes to what it split from.
    /// </summary>
    public double Proportion { get; init; } = double.NaN;

    /// <summary>Tie-breaker for ordering within a slot and in the Window menu (lower comes first).</summary>
    public int Order { get; init; }

    /// <summary>Width of the floating window a <see cref="DockSlot.Float"/> dockable opens as.</summary>
    public double FloatWidth { get; init; } = 800;

    /// <summary>Height of the floating window a <see cref="DockSlot.Float"/> dockable opens as.</summary>
    public double FloatHeight { get; init; } = 600;

    /// <summary>Screen X of the floating window; <see cref="double.NaN"/> (the default) centers it
    /// over the main window.</summary>
    public double FloatX { get; init; } = double.NaN;

    /// <summary>Screen Y of the floating window; <see cref="double.NaN"/> (the default) centers it
    /// over the main window.</summary>
    public double FloatY { get; init; } = double.NaN;

    /// <summary>
    /// When <c>true</c> (the default), there is at most one of this dockable: opening it again focuses
    /// the existing one. When <c>false</c>, every open spawns a fresh instance (its own view-model and
    /// engine resources) — used by the viewport, where each panel drives a separate engine camera.
    /// The workspace disposes a spawned view-model when its panel closes.
    /// </summary>
    public bool Singleton { get; init; } = true;

    /// <summary>
    /// Whether the dockable is listed in the auto-populated Window menu (the default). Settings turns
    /// this off — it is opened from its own Edit ▸ Settings item instead.
    /// </summary>
    public bool ShowInWindowMenu { get; init; } = true;
}
