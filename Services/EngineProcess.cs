using System.Diagnostics;
using System.Globalization;

namespace Toybox.Studio.Services;

/// <summary>
/// A running engine process started by the studio.
/// </summary>
public sealed class EngineProcess : IDisposable
{
    private readonly Process _process;

    private EngineProcess(Process process)
    {
        _process = process;
        _process.EnableRaisingEvents = true;
        _process.Exited += (_, _) => Exited?.Invoke(_process.ExitCode);
    }

    /// <summary>
    /// Raised with the exit code when the process terminates for any reason.
    /// </summary>
    public event Action<int>? Exited;

    public bool IsRunning => !_process.HasExited;

    public int Id => _process.Id;

    /// <summary>
    /// Starts the launcher with the RPC port exposed via TBX_STUDIO_RPC_PORT, hosting the given
    /// app module with the given settings file.
    /// </summary>
    /// <exception cref="InvalidOperationException">The launcher could not be started.</exception>
    public static EngineProcess Start(
        string launcherPath,
        string appModuleName,
        string appSettingsPath,
        bool hidden,
        int rpcPort)
    {
        if (!File.Exists(launcherPath))
            throw new InvalidOperationException($"Engine launcher not found at '{launcherPath}'.");

        var startInfo = new ProcessStartInfo(launcherPath)
        {
            WorkingDirectory = Path.GetDirectoryName(launcherPath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.Environment["TBX_STUDIO_RPC_PORT"] = rpcPort.ToString(CultureInfo.InvariantCulture);
        if (hidden)
            startInfo.ArgumentList.Add("--hidden");

        startInfo.ArgumentList.Add($"--app={appModuleName}");
        startInfo.ArgumentList.Add($"--settings={appSettingsPath}");

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start '{launcherPath}'.");
        return new EngineProcess(process);
    }

    /// <summary>
    /// Starts a launcher in standalone mode: a visible window, no editor RPC port, and not owned by
    /// any studio session. Used to run an exported Release build for the user to test independently.
    /// </summary>
    public static void StartStandalone(string launcherPath, string appModuleName, string appSettingsPath)
    {
        if (!File.Exists(launcherPath))
            throw new InvalidOperationException($"Engine launcher not found at '{launcherPath}'.");

        var startInfo = new ProcessStartInfo(launcherPath)
        {
            WorkingDirectory = Path.GetDirectoryName(launcherPath)!,
            UseShellExecute = true,
        };
        startInfo.ArgumentList.Add($"--app={appModuleName}");
        startInfo.ArgumentList.Add($"--settings={appSettingsPath}");

        if (Process.Start(startInfo) is null)
            throw new InvalidOperationException($"Failed to start '{launcherPath}'.");
    }

    public async Task<bool> WaitForExitAsync(TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await _process.WaitForExitAsync(cts.Token).ContinueOnAnyContext();
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

    /// <summary>
    /// Kills the process and waits for it to fully exit so the OS releases its file handles —
    /// notably on the engine binaries, which are built in-tree and relinked on the next compile.
    /// </summary>
    public async Task KillAndWaitAsync(TimeSpan timeout)
    {
        if (_process.HasExited)
            return;

        _process.Kill(entireProcessTree: true);
        await WaitForExitAsync(timeout).ContinueOnAnyContext();
    }

    public void Dispose()
    {
        _process.Dispose();
    }
}
