using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toybox.Studio.Services.EngineApi;
using Toybox.Studio.Services.World;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Widgets.Toolbar;

/// <summary>
/// One toolbar button: wraps a <see cref="ToolbarItem"/> and exposes its icon/tooltip, whether it is a toggle
/// (a member of a radio group) and its checked state (derived from <see cref="ToolbarState"/>), whether it is
/// shown for the current play mode (its <see cref="GameModeCondition"/>), and a command that runs the tool's
/// (multi-part) command.
/// </summary>
public sealed partial class ToolbarItemViewModel : ObservableObject, IDisposable
{
    private readonly ToolbarItem _item;
    private readonly ToolCommandRunner _runner;
    private readonly ToolbarState _state;
    private readonly EngineWatcher _watcher;

    // Guards against overlapping runs from rapid clicks (e.g. double-clicking Play): while a run is in
    // flight, further clicks are ignored. UI-thread-only, so a plain bool suffices.
    private bool _isRunning;

    public ToolbarItemViewModel(
        ToolbarItem item, ToolCommandRunner runner, ToolbarState state, EngineWatcher watcher)
    {
        _item = item;
        _runner = runner;
        _state = state;
        _watcher = watcher;
        if (IsToggle)
            _state.GroupChanged += OnGroupChanged;
        // Only play-mode-conditional tools track the engine state; Any tools stay visible regardless.
        if (_item.GameMode != GameModeCondition.Any)
            _watcher.StateChanged += OnEngineStateChanged;
    }

    /// <summary>The underlying data model (the reconcile/reorder key).</summary>
    public ToolbarItem Model => _item;

    public string IconName => _item.Icon;

    public string? IconColor => _item.IconColor;

    public string Tooltip => _item.Tooltip;

    /// <summary>Whether this tool belongs to a radio group (renders as a toggle and shows checked state).</summary>
    public bool IsToggle => !string.IsNullOrEmpty(_item.Group);

    /// <summary>Whether this grouped tool is the active member (checked).</summary>
    public bool IsActive => IsToggle && _state.GetActive(_item.Group!) == _item.ActiveStateKey;

    /// <summary>
    /// Whether this tool is shown for the current play mode: <see cref="GameModeCondition.Any"/> tools always;
    /// <see cref="GameModeCondition.Off"/> only while stopped; <see cref="GameModeCondition.On"/> only while playing.
    /// </summary>
    public bool IsVisible => _item.GameMode switch
    {
        GameModeCondition.Off => _watcher.State != EngineState.Playing,
        GameModeCondition.On => _watcher.State == EngineState.Playing,
        _ => true,
    };

    public void Dispose()
    {
        if (IsToggle)
            _state.GroupChanged -= OnGroupChanged;
        if (_item.GameMode != GameModeCondition.Any)
            _watcher.StateChanged -= OnEngineStateChanged;
    }

    // The watcher raises on the UI thread, so re-derive visibility directly.
    private void OnEngineStateChanged(EngineState state) => OnPropertyChanged(nameof(IsVisible));

    [RelayCommand]
    private void Run()
    {
        // Debounce rapid clicks: ignore a click while the previous run is still in flight so a
        // double-click on Play (or a gizmo button) can't launch overlapping command runs.
        if (_isRunning)
            return;

        _isRunning = true;
        RunGuardedAsync().FireAndForget();
    }

    private async Task RunGuardedAsync()
    {
        try
        {
            await _runner.RunAsync(_item.Command, CancellationToken.None).ContinueOnAnyContext();
        }
        finally
        {
            // Reset the guard on the UI thread: it's read by Run() on the UI thread.
            Dispatch.To(DispatchContext.UI, () => _isRunning = false);
        }
    }

    private void OnGroupChanged(string group)
    {
        if (group == _item.Group)
            Dispatch.To(DispatchContext.UI, () => OnPropertyChanged(nameof(IsActive)));
    }
}
