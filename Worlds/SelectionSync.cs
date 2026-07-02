using System;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Worlds;

/// <summary>
/// Pushes the editor's current entity selection to the engine (<c>view.setSelection</c>) whenever it
/// changes, so every viewport highlights the selected entities. Also re-pushes on (re)connect so a
/// freshly launched or attached engine learns the current selection. Resolved once at startup for the
/// app's lifetime.
/// </summary>
public sealed class SelectionSync : IDisposable
{
    private readonly WorldSelection _selection;
    private readonly Engine _engine;
    private readonly Session _session;

    public SelectionSync(WorldSelection selection, Engine engine, Session session)
    {
        _selection = selection;
        _engine = engine;
        _session = session;
        _selection.SelectionChanged += Push;
        _session.StateChanged += OnStateChanged;
    }

    public void Dispose()
    {
        _selection.SelectionChanged -= Push;
        _session.StateChanged -= OnStateChanged;
    }

    private void OnStateChanged(ConnectionState state)
    {
        if (state == ConnectionState.Connected)
            Push();
    }

    private void Push()
    {
        if (_engine.IsConnected)
            _engine.SendNotification(EngineMethods.ViewSetSelection, new { Ids = _selection.SelectedIds })
                .FireAndForget();
    }
}
