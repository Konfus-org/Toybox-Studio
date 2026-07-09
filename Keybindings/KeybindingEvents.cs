namespace Toybox.Studio.Keybindings;

// Every event struct the keybindings domain dispatches, in one place. Handlers implement
// IEventHandler<T> and register with the shared EventDispatcher; nobody references the publisher.

/// <summary>
/// An editor action was invoked — by a matched keybinding, a menu item, or a context-menu item; all
/// three publish this one event, so a feature executes its actions in exactly one place (its
/// handler), whatever triggered them.
/// </summary>
public readonly record struct EditorActionInvoked(string ActionId);

/// <summary>The action registry's contents changed (an action registered or replaced).</summary>
public readonly record struct ActionsChanged;

/// <summary>The editor keymap's bindings changed (loaded, edited, or reset) — gesture hints and the
/// keybindings page re-read <see cref="EditorKeymap.Schemes"/>.</summary>
public readonly record struct KeybindingsChanged;

/// <summary>The gizmo snap-hold key (<see cref="ActionIds.GizmoSnapHold"/>, Ctrl by default) was
/// pressed or released — a held modifier, so it never dispatches an action; the transform tool
/// tracks it to reflect the engine's "snap = enabled XOR held" rule in its snap indicator.</summary>
public readonly record struct SnapHoldChanged(bool Held);
