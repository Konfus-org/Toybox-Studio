using CommunityToolkit.Mvvm.ComponentModel;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Events;
using Toybox.Studio.AppHosting;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Status;

/// <summary>
/// The status bar's state: the engine's <see cref="EngineState"/> and ping round-trip, learned from the
/// dispatched <see cref="EngineStateChanged"/> / <see cref="AppPinged"/> events — no engine references.
/// </summary>
public sealed partial class StatusViewModel :
    ObservableEventSubscriber,
    IEventHandler<EngineStateChanged>,
    IEventHandler<AppPinged>
{
    private EngineState _state = EngineState.Off;

    public StatusViewModel(EventDispatcher events) : base(events)
    {
    }

    public void Handle(in EngineStateChanged evt)
    {
        var state = evt.State;
        Dispatch.To(DispatchContext.UI, () => ApplyState(state));
    }

    public void Handle(in AppPinged evt)
    {
        var roundTrip = evt.RoundTrip;
        Dispatch.To(DispatchContext.UI, () => PingText = $"{roundTrip.TotalMilliseconds:F0} ms");
    }

    [ObservableProperty]
    public partial string StatusText { get; private set; } = "Engine: not connected";

    [ObservableProperty]
    public partial string PingText { get; private set; } = "";

    public bool IsConnected => _state.IsConnected;

    public bool IsLaunching => _state.IsLoading;

    private void ApplyState(EngineState state)
    {
        _state = state;
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(IsLaunching));
        StatusText = state.Phase switch
        {
            EnginePhase.Compiling => "Engine: compiling…",
            EnginePhase.Loading => "Engine: loading…",
            EnginePhase.Ready or EnginePhase.Playing => state.Kind == HostKind.Attached
                ? "Engine: attached"
                : "Engine: connected",
            _ => "Engine: not connected",
        };

        if (!IsConnected)
            PingText = "";
    }
}
