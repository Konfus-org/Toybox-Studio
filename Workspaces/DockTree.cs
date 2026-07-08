using System.Diagnostics.CodeAnalysis;
using Dock.Model.Controls;
using Dock.Model.Core;

namespace Toybox.Studio.Workspaces;

/// <summary>
/// Read-only queries over a Dock layout tree. Every query walks a dock's <c>VisibleDockables</c>
/// recursively; the <see cref="IRootDock"/> overload of <c>TryFind</c> also searches every floating
/// window. Pure traversal — all Dock behavior lives in <see cref="Workspace"/> and
/// <see cref="LayoutManager"/>.
/// </summary>
public static class DockTree
{
    /// <summary>Finds the first dockable under <paramref name="dock"/> matching <paramref name="matches"/>.</summary>
    public static bool TryFind(IDock dock, Func<IDockable, bool> matches, [NotNullWhen(true)] out IDockable? found)
    {
        if (dock.VisibleDockables is { } dockables)
        {
            foreach (var dockable in dockables)
            {
                if (matches(dockable))
                {
                    found = dockable;
                    return true;
                }

                if (dockable is IDock child && TryFind(child, matches, out found))
                    return true;
            }
        }

        found = null;
        return false;
    }

    /// <summary>Finds the first matching dockable in the main layout, then in every floating window;
    /// reports the floating window when the match lives in one.</summary>
    public static bool TryFind(IRootDock root, Func<IDockable, bool> matches,
        [NotNullWhen(true)] out IDockable? found, out IDockWindow? floatingWindow)
    {
        if (TryFind(root, matches, out found))
        {
            floatingWindow = null;
            return true;
        }

        if (root.Windows is { } windows)
        {
            foreach (var window in windows)
            {
                if (window.Layout is { } layout && TryFind(layout, matches, out found))
                {
                    floatingWindow = window;
                    return true;
                }
            }
        }

        found = null;
        floatingWindow = null;
        return false;
    }

    /// <summary>The tool dock with the given id, or null when the layout holds none.</summary>
    public static IToolDock? FindToolDock(IDock dock, string id)
    {
        if (dock is IToolDock toolDock && dock.Id == id)
            return toolDock;

        if (dock.VisibleDockables is { } dockables)
        {
            foreach (var child in dockables)
            {
                if (child is IDock childDock && FindToolDock(childDock, id) is { } match)
                    return match;
            }
        }

        return null;
    }

    /// <summary>The first tool dock in the layout, or null when none survive.</summary>
    public static IToolDock? FirstToolDock(IDock dock)
    {
        if (dock is IToolDock toolDock)
            return toolDock;

        if (dock.VisibleDockables is { } dockables)
        {
            foreach (var child in dockables)
            {
                if (child is IDock childDock && FirstToolDock(childDock) is { } match)
                    return match;
            }
        }

        return null;
    }

    /// <summary>The first proportional dock in the layout, or null when none exists.</summary>
    public static IProportionalDock? FirstProportionalDock(IDock dock)
    {
        if (dock.VisibleDockables is { } dockables)
        {
            foreach (var child in dockables)
            {
                if (child is IProportionalDock proportional)
                    return proportional;
                if (child is IDock childDock && FirstProportionalDock(childDock) is { } match)
                    return match;
            }
        }

        return null;
    }

    /// <summary>
    /// The root's off-layout dockables: the four edge pin strips plus the hidden set. Null-safe — any of
    /// these collections may be unset on a freshly built or partially deserialized layout.
    /// </summary>
    public static IEnumerable<IDockable> PinnedAndHidden(IRootDock root)
    {
        IList<IDockable>?[] collections =
        [
            root.LeftPinnedDockables,
            root.RightPinnedDockables,
            root.TopPinnedDockables,
            root.BottomPinnedDockables,
            root.HiddenDockables,
        ];

        foreach (var collection in collections)
        {
            if (collection is null)
                continue;
            foreach (var dockable in collection)
                yield return dockable;
        }
    }
}
