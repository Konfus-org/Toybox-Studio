using CommunityToolkit.Mvvm.ComponentModel;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Widgets.Status;

public sealed partial class StatusViewModel : ObservableObject
{
    private readonly Session _session;

    public StatusViewModel(Session session)
    {
        _session = session;
        session.StateChanged += state => Dispatch.To(DispatchContext.UI, () => ApplyState(state));
        session.PingMeasured += roundTrip =>
            Dispatch.To(DispatchContext.UI, () => PingText = $"{roundTrip.TotalMilliseconds:F0} ms");
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConnected))]
    [NotifyPropertyChangedFor(nameof(IsLaunching))]
    public partial ConnectionState State { get; private set; } = ConnectionState.Disconnected;

    [ObservableProperty]
    public partial string StatusText { get; private set; } = "Engine: not connected";

    [ObservableProperty]
    public partial string PingText { get; private set; } = "";

    public bool IsConnected => State == ConnectionState.Connected;

    public bool IsLaunching => State == ConnectionState.Launching;

    private void ApplyState(ConnectionState state)
    {
        State = state;
        StatusText = state switch
        {
            ConnectionState.Launching => "Engine: launching…",
            ConnectionState.Connected => _session.Kind == SessionKind.Attached
                ? $"Engine: attached — {_session.ConnectedAppName}"
                : $"Engine: connected — {_session.ConnectedAppName}",
            _ => "Engine: not connected",
        };

        if (state != ConnectionState.Connected)
            PingText = "";
    }
}
