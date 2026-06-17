using System;
using Avalonia.Controls;

namespace Toybox.Studio.Shell.Workspace;

/// <summary>
/// The runtime form of a <see cref="DockableAttribute"/>: everything the menu, the dock factory, and the
/// float/focus logic need to know about one dockable, with <see cref="CreateView"/> resolving the
/// view-model from DI and building a fresh view each time the tool is (re)materialized.
/// </summary>
public sealed class DockableDescriptor
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public string? Icon { get; init; }

    public (double Width, double Height) FloatSize { get; init; }

    public DockSlot Slot { get; init; }

    public double Proportion { get; init; }

    public int Order { get; init; }

    /// <summary>
    /// When <c>true</c>, there is at most one instance (focus-or-open); when <c>false</c>, each open
    /// spawns a fresh instance with its own view-model (registered transient). See
    /// <see cref="DockableAttribute.Singleton"/>.
    /// </summary>
    public bool Singleton { get; init; } = true;

    /// <summary>
    /// Builds a brand-new view. Invoked through Dock's deferred template every time the tool
    /// materializes, so it must return a new control each call — a single live control gets orphaned
    /// when re-parented; the view-model carries all the state. Pass <c>null</c> to bind the view-model
    /// resolved from DI (singletons); pass an explicit instance to bind a specific spawned view-model.
    /// </summary>
    public required Func<object?, Control> CreateView { get; init; }

    /// <summary>
    /// Resolves a fresh view-model from DI. For non-singletons (transient registration) this is a new
    /// instance each call — the spawned instance is created once on open and reused by
    /// <see cref="CreateView"/> across re-templating.
    /// </summary>
    public required Func<object> CreateViewModel { get; init; }
}
