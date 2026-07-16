using Newtonsoft.Json.Linq;

namespace Toybox.Studio.EngineApi;

// Every event struct the engine layer dispatches, in one place; the generic owned-app events (connection
// lifecycle, process exit, ping/freeze, instance detection) live in AppHosting/AppEvents.cs. Handlers
// implement IEventHandler<T> and register with the shared EventDispatcher; nobody references the
// publisher. Events fire on whatever thread raised them unless noted — handlers marshal themselves.

/// <summary>
/// The engine created (or failed to create) a view's shared GPU texture. <see cref="Handle"/> is a
/// Windows global shared D3D11 texture handle the editor's compositor imports directly; a zero handle
/// means GPU sharing was unavailable for this view. Each viewport filters by view <see cref="Name"/>.
/// </summary>
public readonly record struct ViewSurfaceCreated(string Name, long Handle, int Width, int Height, string Format);

/// <summary>A project compile started (true) or finished (false). Drives the engine's coarse
/// <c>Compiling</c> phase; the splash's detailed compile bar rides the generic
/// <see cref="Toybox.Studio.Events.LaunchActivityChanged"/> the build runner also raises.</summary>
public readonly record struct BuildStateChanged(bool IsBuilding);

/// <summary><see cref="Engine.State"/> — the single "what is the engine doing" value — changed.
/// Dispatched on the UI thread.</summary>
public readonly record struct EngineStateChanged(EngineState State);

/// <summary>A "Launch Engine" call-to-action was invoked (from a viewport that is empty because the
/// engine is off — it crashed and gave up auto-restarting, was stopped, or never started). The Projects
/// layer's coordinator handles it by building the active project and launching its engine; the engine
/// layer only reports state and never starts itself, so it merely announces the request. Dispatched on
/// the UI thread.</summary>
public readonly record struct EngineLaunchRequested;

/// <summary>The engine changed one synced value: <see cref="Address"/> identifies the owning object
/// (see <see cref="EngineAddress"/>), <see cref="Key"/> the property. The <see cref="SyncHub"/> routes
/// it to whatever is bound under that address; fires on the RPC listener thread.</summary>
public readonly record struct SyncChanged(string Address, string Key, JToken Value);

/// <summary>The engine raised one synced event: <see cref="Address"/> identifies the owning object,
/// <see cref="Key"/> the event, <see cref="Args"/> its payload. The <see cref="SyncHub"/> routes it to
/// whatever is bound under that address; fires on the RPC listener thread.</summary>
public readonly record struct SyncEventRaised(string Address, string Key, JToken Args);

/// <summary>Which end of an interactive-edit bracket an <see cref="EditTransactionChanged"/> marks.</summary>
public enum EditTransactionPhase
{
    /// <summary>The interaction started; hold history recording until it commits.</summary>
    Begin,

    /// <summary>The interaction landed; record the net change as one undo step and flag dirty.</summary>
    Commit,
}

/// <summary>The engine bracketed one interactive viewport edit (a gizmo drag): <see cref="Phase"/> is
/// <see cref="EditTransactionPhase.Begin"/> as it starts and <see cref="EditTransactionPhase.Commit"/>
/// when it lands, with the synced value changes streamed in between. The focused world owner collapses
/// the run into a single undo step. Fires on the RPC listener thread.</summary>
public readonly record struct EditTransactionChanged(EditTransactionPhase Phase);
