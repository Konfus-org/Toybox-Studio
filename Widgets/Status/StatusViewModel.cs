using CommunityToolkit.Mvvm.ComponentModel;
using Toybox.Studio.Services;

namespace Toybox.Studio.Widgets.Status;

public sealed partial class StatusViewModel : ObservableObject
{
    private readonly EngineSession _session;

    public StatusViewModel(EngineSession session)
    {
        _session = session;
        session.StateChanged += state => Dispatch.To(DispatchContext.UI, () => ApplyState(state));
        session.PingMeasured += roundTrip =>
            Dispatch.To(DispatchContext.UI, () => PingText = $"{roundTrip.TotalMilliseconds:F0} ms");
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConnected))]
    [NotifyPropertyChangedFor(nameof(IsLaunching))]
    public partial EngineConnectionState State { get; private set; } = EngineConnectionState.Disconnected;

    [ObservableProperty]
    public partial string StatusText { get; private set; } = "Engine: not connected";

    [ObservableProperty]
    public partial string PingText { get; private set; } = "";

    public bool IsConnected => State == EngineConnectionState.Connected;

    public bool IsLaunching => State == EngineConnectionState.Launching;

    private void ApplyState(EngineConnectionState state)
    {
        State = state;
        StatusText = state switch
        {
            EngineConnectionState.Launching => "Engine: launching…",
            EngineConnectionState.Connected => _session.Kind == EngineSessionKind.Attached
                ? $"Engine: attached — {_session.ConnectedAppName}"
                : $"Engine: connected — {_session.ConnectedAppName}",
            _ => "Engine: not connected",
        };

        if (state != EngineConnectionState.Connected)
            PingText = "";
    }
}
