using System;
using Avalonia.Controls;

namespace Toybox.Studio.Workspace;

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
    /// Builds a brand-new view with its view-model as DataContext. Invoked through Dock's deferred
    /// template every time the tool materializes, so it must return a new control each call — a single
    /// live control gets orphaned when re-parented; the shared view-model carries all the state.
    /// </summary>
    public required Func<Control> CreateView { get; init; }
}
