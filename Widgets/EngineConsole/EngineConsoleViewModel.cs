using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toybox.Studio.Services;

namespace Toybox.Studio.Widgets.EngineConsole;

public sealed partial class EngineConsoleViewModel : ObservableObject
{
    private const int MaxLines = 1000;

    private readonly object _pendingLock = new();
    private readonly List<EngineLogEntry> _pending = [];
    private bool _isFlushScheduled;

    public EngineConsoleViewModel(EngineSessionService session)
    {
        session.LogReceived += Enqueue;
    }

    public ObservableCollection<EngineLogEntry> Lines { get; } = [];

    [RelayCommand]
    private void Clear()
    {
        Lines.Clear();
    }

    /// <summary>
    /// Batches incoming lines and posts at most one UI flush at a time, so a log flood from the
    /// engine backs up here instead of drowning the dispatcher.
    /// </summary>
    private void Enqueue(EngineLogEntry entry)
    {
        lock (_pendingLock)
        {
            _pending.Add(entry);
            if (_pending.Count > MaxLines)
                _pending.RemoveRange(0, _pending.Count - MaxLines);

            if (_isFlushScheduled)
                return;

            _isFlushScheduled = true;
        }

        Dispatcher.UIThread.Post(Flush, DispatcherPriority.Background);
    }

    private void Flush()
    {
        List<EngineLogEntry> batch;
        lock (_pendingLock)
        {
            batch = [.. _pending];
            _pending.Clear();
            _isFlushScheduled = false;
        }

        if (batch.Count >= MaxLines)
        {
            Lines.Clear();
            batch.RemoveRange(0, batch.Count - MaxLines);
        }

        foreach (var entry in batch)
            Lines.Add(entry);

        while (Lines.Count > MaxLines)
            Lines.RemoveAt(0);
    }
}
