using Toybox.Studio.Keybindings;

namespace Toybox.Studio.Viewport;

/// <summary>
/// Registers the actions generic to <em>any</em> viewport panel, scoped to the hosting dockable the
/// caller names. A viewport-hosting dockable (the world editor's <c>WorldViewerView</c>, an asset
/// preview, …) calls <see cref="Register"/> with its own <see cref="Scheme.For{TViewModel}"/> so these
/// bindings fire while that panel has focus — and the same generic action is bound under each host's
/// scope, never leaking between them.
/// </summary>
/// <remarks>
/// There are no generic viewport actions yet (today's viewport actions — the transform gizmo and render
/// layers — are world-editor specific and registered by <c>WorldViewerToolbarActions</c>). This is the
/// home they land in when they arrive (a "focus selected", "frame all", split/join, camera presets, …):
/// add the <see cref="EditorAction"/> registrations here and every viewport host picks them up under its
/// own scope for free.
/// </remarks>
public static class ViewportActions
{
    /// <summary>Registers the generic viewport actions under <paramref name="scheme"/> (the hosting
    /// dockable's scope, e.g. <c>Scheme.For&lt;WorldViewerViewModel&gt;()</c>).</summary>
    public static void Register(ActionRegistry actions, string scheme)
    {
        _ = actions;
        _ = scheme;
        // No generic viewport actions yet — see the remarks on this class.
    }
}
