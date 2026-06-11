using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Toybox.Studio.Services;

namespace Toybox.Studio.Widgets.EngineStatus;

public sealed partial class EngineStatusViewModel : ObservableObject
{
    private readonly EngineSessionService _session;

    public EngineStatusViewModel(EngineSessionService session)
    {
        _session = session;
        session.StateChanged += state => Dispatcher.UIThread.Post(() => ApplyState(state));
        session.PingMeasured += roundTrip =>
            Dispatcher.UIThread.Post(() => PingText = $"{roundTrip.TotalMilliseconds:F0} ms");
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
            EngineConnectionState.Connected => $"Engine: connected — {_session.ConnectedAppName}",
            _ => "Engine: not connected",
        };

        if (state != EngineConnectionState.Connected)
            PingText = "";
    }
}
