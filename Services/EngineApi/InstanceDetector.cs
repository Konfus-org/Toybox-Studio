using Toybox.Studio.Utils;
using System.Net;
using System.Net.Sockets;

namespace Toybox.Studio.Services.EngineApi;

/// <summary>
/// Watches for an already-running engine on the well-known RPC port while the session is
/// disconnected, so the editor can attach instead of launching its own instance.
/// </summary>
public sealed class InstanceDetector : IDisposable
{
    public const int DefaultEnginePort = 17890;

    private static readonly TimeSpan ProbeInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMilliseconds(250);

    private readonly Session _session;
    private CancellationTokenSource? _cts;

    public InstanceDetector(Session session)
    {
        _session = session;
    }

    /// <summary>
    /// Raised on the edge when a running instance first appears on the port.
    /// </summary>
    public event Action<int>? InstanceDetected;

    /// <summary>
    /// Raised on the edge when the previously detected instance goes away.
    /// </summary>
    public event Action? InstanceLost;

    public bool IsInstanceAvailable { get; private set; }

    public void Start()
    {
        if (_cts is not null)
            return;

        _cts = new CancellationTokenSource();
        Task.Run(() => ProbeLoopAsync(_cts.Token)).FireAndForget();
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    private async Task ProbeLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            // Only probe while disconnected: a connected session either owns the port already
            // or is attached to the instance we'd be probing.
            if (_session.State == ConnectionState.Disconnected)
            {
                var available = await ProbeAsync(ct).ContinueOnAnyContext();
                if (available && !IsInstanceAvailable)
                {
                    IsInstanceAvailable = true;
                    InstanceDetected?.Invoke(DefaultEnginePort);
                }
                else if (!available && IsInstanceAvailable)
                {
                    IsInstanceAvailable = false;
                    InstanceLost?.Invoke();
                }
            }

            try
            {
                await Task.Delay(ProbeInterval, ct).ContinueOnAnyContext();
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private static async Task<bool> ProbeAsync(CancellationToken ct)
    {
        try
        {
            using var probe = new TcpClient();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(ProbeTimeout);
            await probe.ConnectAsync(IPAddress.Loopback, DefaultEnginePort, timeout.Token)
                .ContinueOnAnyContext();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
