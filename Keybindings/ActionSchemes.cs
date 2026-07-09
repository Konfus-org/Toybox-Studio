namespace Toybox.Studio.Keybindings;

/// <summary>
/// The editor keymap's scheme names. A scheme is a scope: <see cref="Global"/> applies everywhere,
/// and a scheme named after a dockable's key (its view-model type name, e.g. <c>ConsoleViewModel</c>)
/// applies while that panel has focus — scoped bindings win over global ones, so the same chord can
/// mean different things in different panels.
/// </summary>
public static class ActionSchemes
{
    public const string Global = "Global";

    /// <summary>The viewport scope. A scheme name IS the dockable's view-model type name (see the
    /// class remarks), so this const must track the Viewport project's <c>ViewportViewModel</c> —
    /// spelled here rather than via typeof so Keybindings never references a panel project.</summary>
    public const string Viewport = "ViewportViewModel";
}
