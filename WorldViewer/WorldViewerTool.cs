using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi.Types.Gizmos;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Events;
using Toybox.Studio.Hosting;
using Toybox.Studio.Keybindings;
using Toybox.Studio.Settings;
using Toybox.Studio.Utils;

namespace Toybox.Studio.WorldViewer;

/// <summary>
/// The world viewport's transform tool — the one source of truth for the active <see cref="GizmoMode"/>
/// and the snapping toggle, however they are driven (toolbar buttons, Q/W/E/R keybindings, menus): it
/// executes the gizmo actions' <see cref="EditorActionInvoked"/>, dispatching one
/// <see cref="WorldViewerToolChanged"/> per real change. Every change pushes the active mode's
/// editor-authored handle set (<see cref="TransformGizmo"/>) plus the snap settings to the engine
/// (<c>view.setGizmo</c>) — and again on every (re)connect, the engine's gizmo being process state.
/// The snap-hold binding rides along as engine key codes resolved from the keymap, so the engine
/// hardcodes no modifier: rebinding <c>gizmo.snapHold</c> re-pushes the new key. Registration of its
/// actions lives with the world viewport (<see cref="WorldViewerToolbarActions"/>); this tool is pure
/// execution. One instance, created and disposed by the composition root.
/// </summary>
public sealed class WorldViewerTool : EventSubscriber,
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

    public WorldViewerTool(
        Engine engine,
        SettingsManager settings,
        EditorKeymap keymap,
        EventDispatcher events)
        : base(events)
    {
        _engine = engine;
        _settings = settings;
        _keymap = keymap;
    }

    /// <summary>The active transform tool; <see cref="GizmoMode.Select"/> out of the box.</summary>
    public GizmoMode Mode { get; private set; } = GizmoMode.Select;

    /// <summary>The gizmo's axis frame — world axes (<see cref="GizmoOrientation.Global"/>) out of the
    /// box, or the primary selection's local axes. The engine derives the basis from this.</summary>
    public GizmoOrientation Orientation { get; private set; } = GizmoOrientation.Global;

    /// <summary>Whether snapping is the default for drags (the toolbar's magnet toggle; the held
    /// snap key momentarily inverts it). Persisted in the editor settings.</summary>
    public bool SnappingEnabled => _settings.Editor.Gizmos.SnapEnabled;

    /// <summary>Whether the snap-hold key (Ctrl by default) is currently held, tracked from the
    /// window's key events. Inverts <see cref="SnappingEnabled"/> exactly as the engine does.</summary>
    public bool SnapHoldHeld { get; private set; }

    /// <summary>Whether a drag would snap right now — the engine's rule, <see cref="SnappingEnabled"/>
    /// XOR <see cref="SnapHoldHeld"/>. Drives the toolbar's snap-amount indicator.</summary>
    public bool EffectiveSnapping => SnappingEnabled ^ SnapHoldHeld;

    /// <summary>The active mode's snap step — translate units, rotate degrees, or scale factor (Select
    /// shares the translate step, its default drag). Reads the matching per-kind setting; write it with
    /// <see cref="SetSnapStep"/>.</summary>
    public double SnapStep
    {
        get
        {
            var gizmos = _settings.Editor.Gizmos;
            return Mode switch
            {
                GizmoMode.Rotate => gizmos.RotateStepDegrees,
                GizmoMode.Scale => gizmos.ScaleStep,
                _ => gizmos.TranslateStep,
            };
        }
    }

    /// <summary>Sets the active mode's snap step (from the toolbar's snap-amount field), then announces,
    /// pushes and persists exactly as the toggle does. A no-op when unchanged, so a scrub that doesn't
    /// cross a step doesn't churn the engine or the settings file.</summary>
    public void SetSnapStep(double value)
    {
        value = Math.Max(0, value);
        var gizmos = _settings.Editor.Gizmos;
        switch (Mode)
        {
            case GizmoMode.Rotate when gizmos.RotateStepDegrees != value:
                gizmos.RotateStepDegrees = value;
                break;
            case GizmoMode.Scale when gizmos.ScaleStep != value:
                gizmos.ScaleStep = value;
                break;
            case GizmoMode.Translate or GizmoMode.Select when gizmos.TranslateStep != value:
                gizmos.TranslateStep = value;
                break;
            default:
                return;
        }

        Events.Dispatch(new WorldViewerToolChanged());
        Push();
        _settings.ApplyProjectAsync().FireAndForget();
    }

    /// <summary>Switches the active tool, announcing and pushing on a real change.</summary>
    public void SetMode(GizmoMode mode)
    {
        if (Mode == mode)
            return;

        Mode = mode;
        Events.Dispatch(new WorldViewerToolChanged());
        Push();
    }

    /// <summary>Flips the snapping toggle: announces and pushes here, then persists to the project's
    /// settings file (a project-scoped section, so through <see cref="SettingsManager.ApplyProjectAsync"/>).</summary>
    public void ToggleSnapping()
    {
        _settings.Editor.Gizmos.SnapEnabled = !SnappingEnabled;
        Events.Dispatch(new WorldViewerToolChanged());
        Push();
        _settings.ApplyProjectAsync().FireAndForget();
    }

    /// <summary>Flips between world (global) and local axis orientation; announces and pushes so the
    /// engine reorients the handles and their drag axes.</summary>
    public void ToggleOrientation()
    {
        Orientation = Orientation == GizmoOrientation.Local ? GizmoOrientation.Global : GizmoOrientation.Local;
        Events.Dispatch(new WorldViewerToolChanged());
        Push();
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
            case ActionIds.GizmoToggleOrientation:
                ToggleOrientation();
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
        Events.Dispatch(new WorldViewerToolChanged());
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
        Events.Dispatch(new WorldViewerToolChanged());
    }

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
            ["orientation"] = Orientation == GizmoOrientation.Local ? "local" : "global",
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
        GizmoHandleKind.Plane => "plane",
        GizmoHandleKind.PlaneScale => "plane_scale",
        _ => "center",
    };

    private static string ToWire(GizmoAxis axis) => axis switch
    {
        GizmoAxis.X => "x",
        GizmoAxis.Y => "y",
        GizmoAxis.Z => "z",
        GizmoAxis.View => "view",
        _ => "all",
    };
}
