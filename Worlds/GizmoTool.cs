using System.Globalization;
using Newtonsoft.Json.Linq;
using Toybox.Studio.AppHosting;
using Toybox.Studio.Assets;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Events;
using Toybox.Studio.Gizmos;
using Toybox.Studio.Keybindings;
using Toybox.Studio.Settings;
using Toybox.Studio.Utils;
using Icon = IconPacks.Avalonia.Lucide.PackIconLucideKind;

namespace Toybox.Studio.Worlds;

/// <summary>
/// The editor's transform tool — the one source of truth for the active <see cref="GizmoMode"/> and
/// the snapping toggle, however they are driven (toolbar buttons, Q/W/E/R keybindings, menus): it
/// registers the gizmo actions and executes their <see cref="EditorActionInvoked"/>, dispatching one
/// <see cref="GizmoToolChanged"/> per real change. Every change pushes the active mode's
/// editor-authored handle set (<see cref="TransformGizmo"/>) plus the snap settings to the engine
/// (<c>view.setGizmo</c>) — and again on every (re)connect, the engine's gizmo being process state.
/// The snap-hold binding rides along as engine key codes resolved from the keymap, so the engine
/// hardcodes no modifier: rebinding <c>gizmo.snapHold</c> re-pushes the new key. One instance,
/// created and disposed by the composition root.
/// </summary>
public sealed class GizmoTool : EventSubscriber,
    IEventHandler<EditorActionInvoked>,
    IEventHandler<ConnectionChanged>,
    IEventHandler<EditorSettingsChanged>,
    IEventHandler<KeybindingsChanged>,
    IEventHandler<SnapHoldChanged>
{
    // A bare-modifier snap-hold binding means "that modifier": holding either physical key should
    // snap, so the bound key's opposite-side twin rides along in the push.
    private static readonly IReadOnlyDictionary<InputKey, InputKey> ModifierTwins =
        new Dictionary<InputKey, InputKey>
        {
            [InputKey.LCtrl] = InputKey.RCtrl,
            [InputKey.RCtrl] = InputKey.LCtrl,
            [InputKey.LShift] = InputKey.RShift,
            [InputKey.RShift] = InputKey.LShift,
            [InputKey.LAlt] = InputKey.RAlt,
            [InputKey.RAlt] = InputKey.LAlt,
            [InputKey.LGui] = InputKey.RGui,
            [InputKey.RGui] = InputKey.LGui,
        };

    private readonly Engine _engine;
    private readonly SettingsManager _settings;
    private readonly EditorKeymap _keymap;

    public GizmoTool(
        ActionRegistry actions,
        Engine engine,
        SettingsManager settings,
        EditorKeymap keymap,
        EventDispatcher events)
        : base(events)
    {
        _engine = engine;
        _settings = settings;
        _keymap = keymap;
        RegisterActions(actions);
    }

    /// <summary>The active transform tool; <see cref="GizmoMode.Select"/> out of the box.</summary>
    public GizmoMode Mode { get; private set; } = GizmoMode.Select;

    /// <summary>Whether snapping is the default for drags (the toolbar's magnet toggle; the held
    /// snap key momentarily inverts it). Persisted in the editor settings.</summary>
    public bool SnappingEnabled => _settings.Editor.Gizmos.SnapEnabled;

    /// <summary>Whether the snap-hold key (Ctrl by default) is currently held, tracked from the
    /// window's key events. Inverts <see cref="SnappingEnabled"/> exactly as the engine does.</summary>
    public bool SnapHoldHeld { get; private set; }

    /// <summary>Whether a drag would snap right now — the engine's rule, <see cref="SnappingEnabled"/>
    /// XOR <see cref="SnapHoldHeld"/>. Drives the toolbar's snap-amount indicator.</summary>
    public bool EffectiveSnapping => SnappingEnabled ^ SnapHoldHeld;

    /// <summary>The active mode's snap step, formatted for the indicator (the rotate step carries a
    /// degree sign, scale a leading ×). Select shares the translate step (its default drag).</summary>
    public string SnapStepLabel
    {
        get
        {
            var gizmos = _settings.Editor.Gizmos;
            return Mode switch
            {
                GizmoMode.Rotate => $"{FormatStep(gizmos.RotateStepDegrees)}°",
                GizmoMode.Scale => $"×{FormatStep(gizmos.ScaleStep)}",
                _ => FormatStep(gizmos.TranslateStep),
            };
        }
    }

    private static string FormatStep(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>Switches the active tool, announcing and pushing on a real change.</summary>
    public void SetMode(GizmoMode mode)
    {
        if (Mode == mode)
            return;

        Mode = mode;
        Events.Dispatch(new GizmoToolChanged());
        Push();
    }

    /// <summary>Flips the snapping toggle: announces, pushes, and persists the setting (the save's
    /// own <see cref="EditorSettingsChanged"/> re-push is a harmless echo).</summary>
    public void ToggleSnapping()
    {
        _settings.Editor.Gizmos.SnapEnabled = !SnappingEnabled;
        Events.Dispatch(new GizmoToolChanged());
        Push();
        _settings.SaveAsync().FireAndForget();
    }

    public void Handle(in EditorActionInvoked evt)
    {
        switch (evt.ActionId)
        {
            case ActionIds.GizmoSelect:
                SetMode(GizmoMode.Select);
                break;
            case ActionIds.GizmoTranslate:
                SetMode(GizmoMode.Translate);
                break;
            case ActionIds.GizmoRotate:
                SetMode(GizmoMode.Rotate);
                break;
            case ActionIds.GizmoScale:
                SetMode(GizmoMode.Scale);
                break;
            case ActionIds.GizmoToggleSnap:
                ToggleSnapping();
                break;
        }
    }

    public void Handle(in ConnectionChanged evt)
    {
        if (evt.State == ConnectionState.Connected)
            Push();
    }

    // The snap steps are edited in the Settings grid (and the toggle can be flipped there too);
    // re-announce so toolbars track it, and re-push so the engine drags use the new steps.
    public void Handle(in EditorSettingsChanged evt)
    {
        Events.Dispatch(new GizmoToolChanged());
        Push();
    }

    // A rebound snap-hold key only exists engine-side through the push; resend it.
    public void Handle(in KeybindingsChanged evt) => Push();

    // The snap-hold key was pressed or released: it inverts effective snapping, so re-announce for
    // the toolbars' snap indicator. The engine reads the held key from the forwarded input itself,
    // so there is nothing to push.
    public void Handle(in SnapHoldChanged evt)
    {
        if (SnapHoldHeld == evt.Held)
            return;

        SnapHoldHeld = evt.Held;
        Events.Dispatch(new GizmoToolChanged());
    }

    private void RegisterActions(ActionRegistry actions)
    {
        actions.Register(new EditorAction
        {
            Id = ActionIds.GizmoSelect,
            Title = "Select Tool",
            Category = "Viewport",
            Icon = Icon.MousePointer2,
            Scheme = ActionSchemes.Viewport,
            DefaultChords = [Chord(InputKey.Q)],
        });
        actions.Register(new EditorAction
        {
            Id = ActionIds.GizmoTranslate,
            Title = "Move Tool",
            Category = "Viewport",
            Icon = Icon.Move3d,
            Scheme = ActionSchemes.Viewport,
            DefaultChords = [Chord(InputKey.W)],
        });
        actions.Register(new EditorAction
        {
            Id = ActionIds.GizmoRotate,
            Title = "Rotate Tool",
            Category = "Viewport",
            Icon = Icon.Rotate3d,
            Scheme = ActionSchemes.Viewport,
            DefaultChords = [Chord(InputKey.E)],
        });
        actions.Register(new EditorAction
        {
            Id = ActionIds.GizmoScale,
            Title = "Scale Tool",
            Category = "Viewport",
            Icon = Icon.Scale3d,
            Scheme = ActionSchemes.Viewport,
            DefaultChords = [Chord(InputKey.R)],
        });
        // A held modifier, not a pressed chord: the dispatcher never matches it (a bare modifier
        // down isn't a chord); its binding exists so the keybindings page can rebind which held key
        // snaps — the engine reads it live from the forwarded input.
        actions.Register(new EditorAction
        {
            Id = ActionIds.GizmoSnapHold,
            Title = "Snap (Hold)",
            Category = "Viewport",
            Icon = Icon.Magnet,
            Scheme = ActionSchemes.Viewport,
            DefaultChords = [Chord(InputKey.LCtrl)],
        });
        actions.Register(new EditorAction
        {
            Id = ActionIds.GizmoToggleSnap,
            Title = "Toggle Snapping",
            Category = "Viewport",
            Icon = Icon.Magnet,
            Scheme = ActionSchemes.Viewport,
        });
    }

    private static KeyChordInputControl Chord(InputKey key) => new() { Key = key };

    // The full gizmo state as one view.setGizmo notification: the active mode's handle set (each
    // handle's kind/axis/extent for interaction, its GizmoRenderer ops for the look) and the snap
    // settings (toggle, per-kind steps, and the snap-hold binding's engine key codes).
    private void Push()
    {
        if (!_engine.IsConnected)
            return;

        var ops = new GizmoOpsConverter();
        var thickness = _settings.Editor.Accessibility.GizmoThickness;
        var handles = new JArray(TransformGizmo.HandlesFor(Mode, thickness).Select(handle => new JObject
        {
            ["kind"] = ToWire(handle.Kind),
            ["axis"] = ToWire(handle.Axis),
            ["extent"] = handle.Extent,
            ["ops"] = ops.Write(handle.Ops),
        }));
        var gizmos = _settings.Editor.Gizmos;
        var payload = new JObject
        {
            ["handles"] = handles,
            ["snap"] = new JObject
            {
                ["enabled"] = gizmos.SnapEnabled,
                ["translate"] = gizmos.TranslateStep,
                ["rotateDeg"] = gizmos.RotateStepDegrees,
                ["scale"] = gizmos.ScaleStep,
                ["keys"] = new JArray(SnapHoldKeys()),
            },
        };
        _engine.SendNotificationAsync(EngineCommands.ViewSetGizmo, payload).FireAndForget();
    }

    private IReadOnlyList<int> SnapHoldKeys()
    {
        if (_keymap.FirstChordFor(ActionIds.GizmoSnapHold) is not { } chord
            || chord.Key == InputKey.Unknown)
            return [];

        var keys = new List<int> { (int)chord.Key };
        if (ModifierTwins.TryGetValue(chord.Key, out var twin))
            keys.Add((int)twin);
        return keys;
    }

    private static string ToWire(GizmoHandleKind kind) => kind switch
    {
        GizmoHandleKind.Arrow => "arrow",
        GizmoHandleKind.Ring => "ring",
        GizmoHandleKind.Knob => "knob",
        _ => "center",
    };

    private static string ToWire(GizmoAxis axis) => axis switch
    {
        GizmoAxis.X => "x",
        GizmoAxis.Y => "y",
        GizmoAxis.Z => "z",
        _ => "all",
    };
}
