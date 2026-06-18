using Toybox.Studio.Utils;
using Toybox.Studio.Services.Dialogs;
using Toybox.Studio.Services.Logging;

namespace Toybox.Studio.Services.EngineApi;

/// <summary>
/// Keeps the editor usable when the engine wedges. The <see cref="Session"/> watchdog raises
/// <see cref="Session.Unresponsive"/> once a still-connected engine stops answering pings for longer than
/// the threshold (a freeze the process-exit / RPC-disconnect signals never catch); this coordinator turns
/// that into a user-facing choice — force-restart the engine or keep waiting — and retracts the prompt if
/// the engine recovers on its own. The editor shell itself stays responsive throughout; only the frozen
/// engine's viewport is affected.
/// </summary>
/// <remarks>
/// The session raises its signals off the UI thread, so everything here marshals to the UI before touching
/// state or showing the modal. Resolved eagerly at startup so it observes the session from the first launch.
/// </remarks>
public sealed class EngineWatchdog
{
    private readonly Session _session;
    private readonly Logger _log;

    // Touched only on the UI thread. The CTS, when set, dismisses the open prompt if the engine recovers.
    private bool _prompting;
    private CancellationTokenSource? _recoveredCts;

    public EngineWatchdog(Session session, Logger log)
    {
        _session = session;
        _log = log;
        _session.Unresponsive += () => Dispatch.To(DispatchContext.UI, OnUnresponsive);
        _session.Responsive += () => Dispatch.To(DispatchContext.UI, OnResponsive);
    }

    private void OnUnresponsive()
    {
        if (_prompting)
            return;

        _prompting = true;
        PromptAsync().FireAndForget();
    }

    private void OnResponsive()
    {
        // The engine answered again (or the session tore it down): retract the open prompt.
        _recoveredCts?.Cancel();
    }

    private async Task PromptAsync()
    {
        using var recovered = new CancellationTokenSource();
        _recoveredCts = recovered;
        try
        {
            var forceRestart = await Popups.ConfirmAsync(
                "Engine Not Responding",
                "The engine has stopped responding and may be frozen. You can force-restart it — any "
                    + "unsaved changes in the running world will be lost — or keep waiting for it to recover.",
                confirmText: "Force Restart",
                cancelText: "Keep Waiting",
                dismiss: recovered.Token).ContinueOnSameContext();

            if (recovered.IsCancellationRequested)
            {
                _log.Info("Engine recovered; dismissed the unresponsive prompt.");
                return;
            }

            if (forceRestart)
            {
                _log.Warning("Force-restarting the unresponsive engine…");
                await _session.ForceRestartAsync().ContinueOnSameContext();
            }
            else
            {
                _log.Info("Keeping the unresponsive engine; will ask again if it stays frozen.");
                _session.SnoozeWatchdog();
            }
        }
        finally
        {
            _recoveredCts = null;
            _prompting = false;
        }
    }
}
