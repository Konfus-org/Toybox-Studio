using System.Diagnostics;
using System.Globalization;

namespace Toybox.Studio.Services;

/// <summary>A running engine process started by the studio.</summary>
public sealed class EngineProcess : IDisposable
{
    private readonly Process _process;

    private EngineProcess(Process process)
    {
        _process = process;
        _process.EnableRaisingEvents = true;
        _process.Exited += (_, _) => Exited?.Invoke(_process.ExitCode);
    }

    /// <summary>Raised with the exit code when the process terminates for any reason.</summary>
    public event Action<int>? Exited;

    public bool IsRunning => !_process.HasExited;

    public int Id => _process.Id;

    /// <summary>Starts the launcher with the RPC port exposed via TBX_STUDIO_RPC_PORT.</summary>
    /// <exception cref="InvalidOperationException">The launcher could not be started.</exception>
    public static EngineProcess Start(EngineLaunchOptions options, int rpcPort)
    {
        if (!File.Exists(options.LauncherPath))
            throw new InvalidOperationException($"Engine launcher not found at '{options.LauncherPath}'.");

        var startInfo = new ProcessStartInfo(options.LauncherPath)
        {
            WorkingDirectory = options.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.Environment["TBX_STUDIO_RPC_PORT"] = rpcPort.ToString(CultureInfo.InvariantCulture);
        if (options.HideEngineWindow)
            startInfo.ArgumentList.Add("--hidden");

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start '{options.LauncherPath}'.");
        return new EngineProcess(process);
    }

    public async Task<bool> WaitForExitAsync(TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await _process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    public void Kill()
    {
        if (!_process.HasExited)
            _process.Kill(entireProcessTree: true);
    }

    public void Dispose()
    {
        _process.Dispose();
    }
}
