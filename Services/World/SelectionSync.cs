using System;
using Toybox.Studio.Services.EngineApi;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Services.World;

/// <summary>
/// Pushes the editor's current entity selection to the engine (<c>view.setSelection</c>) whenever it
/// changes, so every viewport highlights the selected entities. Also re-pushes on (re)connect so a
/// freshly launched or attached engine learns the current selection. Resolved once at startup for the
/// app's lifetime.
/// </summary>
public sealed class SelectionSync : IDisposable
{
    private readonly WorldSelection _selection;
    private readonly EngineRpc _engine;
    private readonly Session _session;

    public SelectionSync(WorldSelection selection, EngineRpc engine, Session session)
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
            _engine.SetSelectionAsync(_selection.SelectedIds).FireAndForget();
    }
}
