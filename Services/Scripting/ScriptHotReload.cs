using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Toybox.Studio.Services.Logging;

namespace Toybox.Studio.Services.Scripting;

/// <summary>
/// The script editor's hot-reload toggle (the lightning-bolt control). When enabled, saving a script is meant
/// to recompile the scripts plugin and hot-swap it in the running engine. For now it's a stub: it only logs
/// the request, leaving the actual compile + reload wiring (the existing plugin-reload path) for later.
/// A single shared instance backs the bolt on every editor surface, so the toggle is one global setting.
/// </summary>
public sealed partial class ScriptHotReload : ObservableObject
{
    private readonly Logger _log;

    public ScriptHotReload(Logger log)
    {
        _log = log;
    }

    /// <summary>When on, a save triggers <see cref="NotifySaved"/>'s reload (currently a stub).</summary>
    [ObservableProperty]
    public partial bool Enabled { get; set; }

    /// <summary>Hook the editors call after a successful save; performs the reload when enabled.</summary>
    public void NotifySaved(string path)
    {
        if (!Enabled)
            return;

        // Stub: the compile + plugin hot-swap is not wired yet.
        _log.Info($"Hot reload requested for {Path.GetFileName(path)} (stub — not yet implemented).");
    }
}
