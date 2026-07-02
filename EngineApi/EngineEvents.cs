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

/// <summary>A project compile started (true) or finished (false).</summary>
public readonly record struct BuildStateChanged(bool IsBuilding);

/// <summary><see cref="Engine.State"/> — the single "what is the engine doing" value — changed.
/// Dispatched on the UI thread.</summary>
public readonly record struct EngineStateChanged(EngineState State);
