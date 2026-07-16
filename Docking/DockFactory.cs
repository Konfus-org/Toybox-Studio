using Dock.Avalonia.Controls;
using Dock.Model.Avalonia;
using Dock.Model.Controls;
using Dock.Model.Core;

namespace Toybox.Studio.Docking;

/// <summary>
/// The Dock integration seam, and the workspace's ONLY <see cref="Factory"/>: dock models keep a
/// back-reference to the factory that initialized them (the chrome invokes it, its events carry the
/// runtime), so exactly one instance exists — composed by the <see cref="WorkspaceViewModel"/> and held
/// as a private implementation detail by the <see cref="Workspace"/> (window operations + runtime
/// events), the <see cref="LayoutManager"/> (tree construction + InitLayout), and the workspace itself
/// (the <c>DockControl.Factory</c> assignment). Nothing here is Studio behavior — only the hardening
/// Dock needs to run cleanly.
/// </summary>
internal sealed class DockFactory : Factory
{
    public DockFactory()
    {
        // Without a host-window factory Dock has nothing to put a dragged-out panel into, so floating it
        // just makes it vanish. Hand it our HostWindow subclass (Dock's DockFluentTheme still styles it)
        // as the default factory for floated windows; it clamps itself onto a visible screen on open so a
        // float or a restored layout can't strand the title bar off-screen. (HostWindowLocator is a per-id
        // dictionary for overrides; the drag-float path falls back to DefaultHostWindowLocator.)
        DefaultHostWindowLocator = () => new ClampedHostWindow();

        // Stamp capabilities on every dockable the moment Dock initializes it — at startup (InitLayout) and
        // at runtime (a float/drag/split has the library create fresh root/tool docks we never build or
        // attach content over). Without this their Fluent chrome binds into a null DockCapabilityPolicy /
        // DockCapabilityOverrides and floods the log with "Value is null" binding errors.
        DockableInit += (_, args) =>
        {
            if (args.Dockable is { } dockable)
                EnsureDockCapabilities(dockable);
        };
    }

    /// <summary>
    /// Null-guarded close: Dock's chrome buttons bind <c>CloseDockable</c> with <c>ActiveDockable</c>
    /// as the parameter, which is null on an empty dock — the base implementation dereferences it
    /// unconditionally and takes the whole app down.
    /// </summary>
    public override void CloseDockable(IDockable dockable)
    {
        if (dockable is null)
            return;

        base.CloseDockable(dockable);
    }

    // Dock's Fluent chrome binds straight into each dockable's capability override (and each dock's policy).
    // Those objects are null by design — null means "inherit" — but a null trips a binding error for every
    // panel each time its chrome is templated, which floods the log. Hand each dockable an empty instance:
    // all fields stay null, so capability resolution is unchanged, but the theme now has something to bind.
    public static void EnsureDockCapabilities(IDockable dockable)
    {
        dockable.DockCapabilityOverrides ??= new DockCapabilityOverrides();
        if (dockable is IDock dock)
            dock.DockCapabilityPolicy ??= new DockCapabilityPolicy();
        if (dockable is IRootDock root)
            root.RootDockCapabilityPolicy ??= new DockCapabilityPolicy();
    }
}
