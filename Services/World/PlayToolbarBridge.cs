using System;
using Toybox.Studio.Services.EngineApi;

namespace Toybox.Studio.Services.World;

/// <summary>
/// Bridges the engine's pause state (from <see cref="Session"/>) to the generic <see cref="ToolbarState"/> the
/// data-driven toolbar binds to, so the game transport's Pause/Resume toggle shows checked while paused.
/// Mirrors <see cref="GizmoToolbarBridge"/>; resolved once at startup.
/// </summary>
public sealed class PlayToolbarBridge : IDisposable
{
    private const string Group = "transport";

    private readonly Session _session;
    private readonly ToolbarState _state;

    public PlayToolbarBridge(Session session, ToolbarState state)
    {
        _session = session;
        _state = state;
        _session.PausedChanged += Sync;
        Sync(_session.IsPaused);
    }

    public void Dispose() => _session.PausedChanged -= Sync;

    private void Sync(bool isPaused) =>
        _state.SetActive(Group, isPaused ? "transport:paused" : "transport:running");
}
