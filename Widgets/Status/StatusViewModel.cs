using Toybox.Studio.Utils;
using CommunityToolkit.Mvvm.ComponentModel;
using Toybox.Studio.Services.EngineApi;

namespace Toybox.Studio.Widgets.Status;

public sealed partial class StatusViewModel : ObservableObject
{
    private readonly Session _session;

    public StatusViewModel(Session session, EngineWatcher watcher)
    {
        _session = session;
        watcher.StateChanged += state => Dispatch.To(DispatchContext.UI, () => ApplyState(state));
        session.PingMeasured += roundTrip =>
            Dispatch.To(DispatchContext.UI, () => PingText = $"{roundTrip.TotalMilliseconds:F0} ms");
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConnected))]
    [NotifyPropertyChangedFor(nameof(IsLaunching))]
    public partial EngineState State { get; private set; } = EngineState.Off;

    [ObservableProperty]
    public partial string StatusText { get; private set; } = "Engine: not connected";

    [ObservableProperty]
    public partial string PingText { get; private set; } = "";

    public bool IsConnected => State is EngineState.Ready or EngineState.Playing;

    public bool IsLaunching => State is EngineState.Compiling or EngineState.Loading;

    private void ApplyState(EngineState state)
    {
        State = state;
        StatusText = state switch
        {
            EngineState.Compiling => "Engine: compiling…",
            EngineState.Loading => "Engine: loading…",
            EngineState.Ready or EngineState.Playing => _session.Kind == SessionKind.Attached
                ? $"Engine: attached — {_session.ConnectedAppName}"
                : $"Engine: connected — {_session.ConnectedAppName}",
            _ => "Engine: not connected",
        };

        if (!IsConnected)
            PingText = "";
    }
}
