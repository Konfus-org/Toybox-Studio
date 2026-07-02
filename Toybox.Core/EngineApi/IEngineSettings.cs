namespace Toybox.Studio.EngineApi;

/// <summary>
/// The engine-related editor settings the engine connection needs (locating, launching, and supervising
/// the engine process). Inverts the engine layer's dependency on the settings layer: an adapter over the
/// settings manager supplies these, so <see cref="Session"/>/<see cref="EngineLocator"/> stay in the core.
/// </summary>
public interface IEngineSettings
{
    /// <summary>The configured engine source-tree path (read at startup, written when the user picks one).</summary>
    string SourcePath { get; set; }

    /// <summary>Whether the launched engine's OS window is hidden.</summary>
    bool HideEngineWindow { get; }

    /// <summary>How long to wait for the launched engine to accept an RPC connection.</summary>
    int ConnectTimeoutSeconds { get; }

    /// <summary>Whether a crashed owned engine should be auto-restarted.</summary>
    bool RestartOnCrash { get; }

    /// <summary>Persists the editor settings (used after writing <see cref="SourcePath"/>).</summary>
    Task SaveAsync();
}
