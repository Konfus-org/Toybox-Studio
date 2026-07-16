namespace Toybox.Studio.Keybindings;

/// <summary>
/// An editor keymap scheme (scope) name. <see cref="Global"/> applies everywhere; a scoped scheme is
/// named after the dockable it belongs to — its view-model type name, exactly what
/// <c>KeybindingDispatcher</c> derives from the focused view's <c>[Dockable]</c>. Scoped bindings win
/// over global ones, so the same chord can mean different things in different panels.
///
/// Non-global actions are registered by the feature that owns them, which names its scope with
/// <see cref="For{TViewModel}"/> — the name tracks the view-model type under rename with no
/// hand-spelled literal (and Keybindings still references no panel project: the caller supplies the
/// type from its own assembly).
/// </summary>
public static class Scheme
{
    public const string Global = "Global";

    /// <summary>The scope name for a dockable's view-model type — its type name.</summary>
    public static string For<TViewModel>() => typeof(TViewModel).Name;
}
