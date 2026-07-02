using System;
using System.Threading;
using System.Threading.Tasks;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Project;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Worlds;

/// <summary>
/// Owns the editor's runtime state: the single active editing <see cref="World"/> and the play/pause facade.
/// It is the replacement for the old WorldManager — but where that mixed world ownership with the snapshot
/// model, this keeps the live <see cref="World"/> (now an asset that owns its own entities, dirty bit, refresh
/// and save) and adds only the cross-cutting concerns a world can't own alone: which world is active, opening a
/// different one, and the play/runtime state.
///
/// Play/pause is surfaced here (delegating the engine transition to <see cref="Session"/>, which keeps the
/// process lifetime) so the rest of the editor asks one place "are we playing?". The active world's
/// dirty-while-playing rule is wired through <see cref="World.GuardDirtyWhilePlaying"/>.
/// </summary>
public sealed class GameState
{
    private readonly Session _session;

    public GameState(Session session, Engine engine, IEngineSyncScheduler scheduler)
    {
        _session = session;
        Active = World.ForActive(this, engine, scheduler);

        // Edits made while the game plays hit the engine's throwaway play snapshot, so they don't dirty the world.
        Active.GuardDirtyWhilePlaying(() => session.IsPlaying);

        // A fresh session reloads the world from disk; a lost one empties it.
        session.StateChanged += OnSessionStateChanged;
        // The engine's transform gizmo edits entities directly; mirror that back into the editor.
        engine.TransformEdited += OnTransformEdited;
        // Re-surface the engine's play-state transitions as our own.
        session.PlayingChanged += playing => PlayingChanged?.Invoke(playing);
        session.PausedChanged += paused => PausedChanged?.Invoke(paused);
    }

    /// <summary>The active editing world — the root of the object graph the editor edits. A single persistent
    /// instance for the session; opening a different world (<see cref="OpenWorldAsync"/>) reloads it in place.</summary>
    public World Active { get; }

    /// <summary>Whether the engine is currently in play mode (running the game loop) vs editor mode.</summary>
    public bool IsPlaying => _session.IsPlaying;

    /// <summary>Whether the running game is paused.</summary>
    public bool IsPaused => _session.IsPaused;

    /// <summary>Raised when play mode is entered (true) or exited (false).</summary>
    public event Action<bool>? PlayingChanged;

    /// <summary>Raised when the running game is paused (true) or resumed (false).</summary>
    public event Action<bool>? PausedChanged;

    /// <summary>Enters play mode (the engine snapshots its world and runs the game loop).</summary>
    public Task PlayAsync() => _session.StartPlayAsync();

    /// <summary>Exits play mode (the engine restores the pre-play world and stops simulating).</summary>
    public Task StopAsync() => _session.StopPlayAsync();

    /// <summary>Toggles pause on the running game.</summary>
    public Task TogglePauseAsync() => _session.SetPausedAsync(!_session.IsPaused);

    /// <summary>Opens a world asset (by handle) as the active editing world, replacing the current one, then
    /// re-pulls so the tree/inspector and every viewport reflect it.</summary>
    public Task<Result> OpenWorldAsync(AssetHandle worldAsset, CancellationToken ct = default) =>
        Active.OpenAsync(worldAsset, ct);

    private void OnTransformEdited()
    {
        // The engine's gizmo edited an entity directly (not through the reflected field path), so signal the edit
        // to dirty the world, then re-pull.
        Active.NotifyEdited();
        Active.RefreshAsync().FireAndForget();
    }

    private void OnSessionStateChanged(ConnectionState state)
    {
        if (state == ConnectionState.Connected)
            Active.RefreshAsync().FireAndForget();
        else
            Active.Clear();
    }
}
