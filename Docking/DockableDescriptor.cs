using Avalonia.Controls;
using Toybox.Studio.Utils.Attributes;

namespace Toybox.Studio.Docking;

/// <summary>
/// The runtime form of a <see cref="DockableAttribute"/>: everything the menu, the dock factory, and the
/// float/focus logic need to know about one dockable, with <see cref="CreateViewModel"/> invoking the
/// composition root's authored factory and <see cref="CreateView"/> building a fresh view each time the
/// tool is (re)materialized.
/// </summary>
public sealed class DockableDescriptor
{
    /// <summary>
    /// The stable string key derived from <see cref="ViewModelType"/> — used as the Dock tool id in persisted
    /// layouts and for dedupe/focus matching. Not authored: a dockable is identified by its view-model type
    /// (<see cref="ViewModelType"/>); this is the internal persistence form Dock needs.
    /// </summary>
    public required string Key { get; init; }

    public required string Title { get; init; }

    public Icon Icon { get; init; }

    public DockSlot Slot { get; init; }

    /// <summary>The descriptor this dockable docks relative to in the default layout, or null for the
    /// main window — the resolved, validated form of <see cref="DockableAttribute.Parent"/>.</summary>
    public DockableDescriptor? Parent { get; internal set; }

    public double Proportion { get; init; }

    public int Order { get; init; }

    /// <summary>The floating window's bounds for a <see cref="DockSlot.Float"/> dockable; NaN X/Y means
    /// "center over the main window".</summary>
    public (double X, double Y, double Width, double Height) FloatBounds { get; init; }

    /// <summary>
    /// When <c>true</c>, there is at most one instance (focus-or-open); when <c>false</c>, each open
    /// spawns a fresh instance with its own view-model, disposed when its panel closes. See
    /// <see cref="DockableAttribute.Singleton"/>.
    /// </summary>
    public bool Singleton { get; init; } = true;

    /// <summary>Whether the auto-populated Window menu lists this dockable (Settings opts out; it lives
    /// under Edit).</summary>
    public bool ShowInWindowMenu { get; init; } = true;

    /// <summary>
    /// Builds a brand-new view bound to the given view-model. Invoked through Dock's deferred template
    /// every time the tool materializes, so it must return a new control each call — a single live
    /// control gets orphaned when re-parented; the view-model carries all the state.
    /// </summary>
    public required Func<object, Control> CreateView { get; init; }

    /// <summary>
    /// Creates the view-model for one opened panel, through the factory the composition root authored
    /// in <see cref="DockableFactories"/>. The <see cref="Workspace"/> calls this once per open and
    /// keeps the instance in its private table across re-templating.
    /// </summary>
    public required Func<object> CreateViewModel { get; init; }

    /// <summary>The view-model's static type — the dockable's identity (<c>OpenDockable&lt;T&gt;</c>,
    /// parent references, the persistence <see cref="Key"/> all key on it).</summary>
    public required Type ViewModelType { get; init; }
}
