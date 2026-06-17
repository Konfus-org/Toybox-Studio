using System;
using Avalonia.Threading;
using Toybox.Studio.Services.EngineApi;
using Toybox.Studio.Services.World;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Widgets.Ecs;

/// <summary>
/// The editor's decision of WHEN to pull live engine state into the inspector — it replaces the inspector
/// view self-polling. While the game plays, the running game mutates entities, so this pulls the selected
/// entity's state on selection change and on a coarse cadence; when the game is stopped the editor world is
/// static and the inspector is the source of truth, so nothing is pulled. The pull itself defers to an
/// in-progress edit (see <see cref="WorldViewModel.RefreshSelectedEntityAsync"/>).
///
/// Resolved once at startup so it is alive for the app's lifetime; it holds the shared inspector view-model.
/// </summary>
public sealed class InspectorRefreshCoordinator : IDisposable
{
    // Coarse on purpose: live enough to track the running game without churning the grid every frame.
    private static readonly TimeSpan Cadence = TimeSpan.FromMilliseconds(500);

    private readonly WorldViewModel _world;
    private readonly WorldSelection _selection;
    private readonly DispatcherTimer _timer;

    private bool _playing;

    public InspectorRefreshCoordinator(WorldViewModel world, WorldSelection selection, Session session)
    {
        _world = world;
        _selection = selection;

        _timer = new DispatcherTimer { Interval = Cadence };
        _timer.Tick += (_, _) => Pull();

        // PlayingChanged can resume off the UI thread; the timer and view-model touch must marshal to UI.
        session.PlayingChanged += playing => Dispatch.To(DispatchContext.UI, () => OnPlayingChanged(playing));
        selection.SelectionChanged += _ => Dispatch.To(DispatchContext.UI, UpdateRunning);
        _playing = session.IsPlaying;
        UpdateRunning();
    }

    public void Dispose() => _timer.Stop();

    private void OnPlayingChanged(bool playing)
    {
        _playing = playing;
        UpdateRunning();
    }

    // Pull exactly when useful: the game is playing and an entity is selected. Starting also pulls once
    // immediately so selecting (or pressing Play) shows live values without waiting for the first tick.
    private void UpdateRunning()
    {
        if (_playing && _selection.SelectedId is not null)
        {
            if (!_timer.IsEnabled)
                _timer.Start();
            Pull();
        }
        else
        {
            _timer.Stop();
        }
    }

    private void Pull() => _world.RefreshSelectedEntityAsync().FireAndForget();
}
