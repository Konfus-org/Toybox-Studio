using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia;
using System.Reflection;
using Toybox.Studio.EngineApi.Types.Assets;
using Toybox.Studio.Events;
using Toybox.Studio.Input;
using Toybox.Studio.Utils.Attributes;

namespace Toybox.Studio.Keybindings;

/// <summary>
/// Matches window key-downs against the <see cref="EditorKeymap"/> and publishes the matched
/// action's <see cref="EditorActionInvoked"/> — the keyboard half of the one invocation path menus
/// share. Scoping is Blender-style: a scheme named after the focused dockable applies first, then
/// the global scheme, so panels can shadow global chords. Two guards keep typing and the fly camera
/// working: over a text input only chords carrying a command modifier (or a function key) fire, and
/// over an engine input surface (the viewport) bare keys stay with the engine while it is being
/// navigated (a pointer button held) — otherwise the panel's own scoped bindings may claim them
/// (the gizmo tools' Q/W/E/R), never global ones. Modified chords always work. Attach it to the
/// window with <see cref="KeybindingScope"/>.
/// </summary>
public sealed class KeybindingDispatcher
{
    // Dockable identity per view type (the [Dockable] view's view-model type name), resolved once —
    // the focus walk runs per keypress.
    private static readonly Dictionary<Type, string?> ScopeCache = [];

    private readonly EditorKeymap _keymap;
    private readonly EventDispatcher _events;

    // The last-published snap-hold held state, so a key event only dispatches on a real flip.
    private bool _snapHoldHeld;

    public KeybindingDispatcher(EditorKeymap keymap, EventDispatcher events)
    {
        _keymap = keymap;
        _events = events;
    }

    /// <summary>Runs the key-down through the keymap; true (and the matched action published) when a
    /// binding claimed it — the caller marks the event handled.</summary>
    public bool TryHandle(KeyEventArgs e)
    {
        if (KeyChordGestures.FromKey(e.Key, e.KeyModifiers) is not { } pressed)
            return false;

        // A capturing chord box is ASSIGNING this chord — it must not invoke anything.
        if (e.Source is IKeyCaptureSurface { IsCapturingKeys: true })
            return false;

        // A bare modifier going down is never a dispatched chord — modifiers are hold-state (the
        // gizmo's snap-hold binding is one), and claiming the key-down here would keep the hold from
        // ever reaching its consumer (the engine reads it from the forwarded input).
        if (IsModifierKey(pressed))
            return false;

        var commandModified = pressed.Ctrl || pressed.Alt || pressed.Gui;
        var focus = ExamineFocus(e.Source);

        // Typing owns bare keys and shifted characters; function keys are never typed, so they pass.
        if (focus.IsTextInput && !commandModified && !IsFunctionKey(pressed))
            return false;

        // The focused engine input surface (the viewport): while navigating — a pointer button held,
        // so the same bare keys are steering the camera — bare keys stay with the engine. Otherwise
        // the dockable's OWN scoped bindings may claim them (the gizmo tools' Q/W/E/R); the global
        // scheme still never shadows engine keys, and an unmatched bare key flows on to the engine
        // exactly as before (the dispatcher tunnels ahead of the input capture).
        if (focus.IsEngineInput && !commandModified)
        {
            if (focus.IsNavigating || focus.Scope is null)
                return false;
            return TryMatch(SchemeNamed(focus.Scope), pressed);
        }

        return TryMatch(ApplicableSchemes(focus.Scope), pressed);
    }

    /// <summary>
    /// Refreshes the snap-hold key's held state from a key event and publishes a
    /// <see cref="SnapHoldChanged"/> when it flips — called for both key-down and key-up (the
    /// tunneling handler feeds both). The snap-hold binding (<see cref="ActionIds.GizmoSnapHold"/>) is a
    /// modifier out of the box, read from the event's authoritative modifier bitmask so a missed
    /// key-up can't stick it on; a non-modifier rebind falls back to matching the key up/down.
    /// </summary>
    public void TrackSnapHold(KeyEventArgs e, bool isDown)
    {
        if (_keymap.FirstChordFor(ActionIds.GizmoSnapHold) is not { } hold
            || hold.Key == EngineApi.InputKey.Unknown)
        {
            SetSnapHoldHeld(false);
            return;
        }

        bool held;
        if (ModifierFlagFor(hold.Key) is { } flag)
            held = e.KeyModifiers.HasFlag(flag);
        else if (KeyChordGestures.FromKey(e.Key, KeyModifiers.None)?.Key == hold.Key)
            held = isDown;
        else
            return; // An unrelated key; the hold state is unchanged.

        SetSnapHoldHeld(held);
    }

    private void SetSnapHoldHeld(bool held)
    {
        if (_snapHoldHeld == held)
            return;

        _snapHoldHeld = held;
        _events.Dispatch(new SnapHoldChanged(held));
    }

    // The Avalonia modifier flag a held modifier key belongs to (null for a non-modifier key).
    private static KeyModifiers? ModifierFlagFor(EngineApi.InputKey key) => key switch
    {
        EngineApi.InputKey.LCtrl or EngineApi.InputKey.RCtrl => KeyModifiers.Control,
        EngineApi.InputKey.LShift or EngineApi.InputKey.RShift => KeyModifiers.Shift,
        EngineApi.InputKey.LAlt or EngineApi.InputKey.RAlt => KeyModifiers.Alt,
        EngineApi.InputKey.LGui or EngineApi.InputKey.RGui => KeyModifiers.Meta,
        _ => null,
    };

    // Publishes the first action bound to the pressed chord across the given schemes; true when one
    // claimed it.
    private bool TryMatch(IEnumerable<InputScheme> schemes, KeyChordInputControl pressed)
    {
        foreach (var scheme in schemes)
        {
            foreach (var action in scheme.Actions)
            {
                foreach (var binding in action.Bindings)
                {
                    if (binding.Control is KeyChordInputControl chord && chord == pressed)
                    {
                        _events.Dispatch(new EditorActionInvoked(action.Name));
                        return true;
                    }
                }
            }
        }

        return false;
    }

    // The focused dockable's scheme shadows the global one, so it is searched first.
    private IEnumerable<InputScheme> ApplicableSchemes(string? scope)
    {
        if (scope is not null)
        {
            foreach (var scheme in SchemeNamed(scope))
                yield return scheme;
        }

        foreach (var scheme in SchemeNamed(Scheme.Global))
            yield return scheme;
    }

    private IEnumerable<InputScheme> SchemeNamed(string name)
    {
        foreach (var scheme in _keymap.Schemes)
            if (scheme.Name == name)
                yield return scheme;
    }

    // One walk up from the event source: is the key headed for a text input, an engine input surface
    // (a control forwarding to the game), is that surface mid-navigation (a pointer button held on
    // it), and which dockable's scope does it sit in.
    private static (bool IsTextInput, bool IsEngineInput, bool IsNavigating, string? Scope) ExamineFocus(
        object? source)
    {
        var isTextInput = source is TextBox;
        var isEngineInput = false;
        var isNavigating = false;
        string? scope = null;

        for (var visual = source as Visual; visual is not null; visual = visual.GetVisualParent())
        {
            isEngineInput |= visual is IInputSink;
            if (visual is Control control)
            {
                isNavigating |= InputBindingBehavior.GetIsPointerEngaged(control);
                scope ??= DockableScopeOf(control.GetType());
            }
        }

        return (isTextInput, isEngineInput, isNavigating, scope);
    }

    private static string? DockableScopeOf(Type viewType)
    {
        lock (ScopeCache)
        {
            if (ScopeCache.TryGetValue(viewType, out var cached))
                return cached;

            // The dockable's identity is its view-model type name — the explicit ViewModel on the
            // attribute, or the XxxView → XxxViewModel convention the dockable catalog uses.
            string? scope = null;
            if (viewType.GetCustomAttribute<DockableAttribute>() is { } dockable)
            {
                scope = dockable.ViewModel?.Name
                        ?? (viewType.Name.EndsWith("View", StringComparison.Ordinal)
                            ? viewType.Name[..^4] + "ViewModel"
                            : viewType.Name + "ViewModel");
            }

            ScopeCache[viewType] = scope;
            return scope;
        }
    }

    private static bool IsFunctionKey(KeyChordInputControl chord) =>
        chord.Key is >= EngineApi.InputKey.F1 and <= EngineApi.InputKey.F12;

    // The modifier keys themselves (LCtrl..RGui — the engine's contiguous HID block).
    private static bool IsModifierKey(KeyChordInputControl chord) =>
        chord.Key is >= EngineApi.InputKey.LCtrl and <= EngineApi.InputKey.RGui;
}
