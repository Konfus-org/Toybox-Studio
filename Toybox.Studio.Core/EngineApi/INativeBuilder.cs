namespace Toybox.Studio.EngineApi;

/// <summary>
/// The native project build the engine <see cref="Session"/> drives before launch, inverting the engine
/// layer's dependency on the concrete project builder: it compiles the current project and reports its
/// "building" phase so the session can fold that into its busy state.
/// </summary>
public interface INativeBuilder
{
    /// <summary>Raised when a build starts (true) or finishes (false).</summary>
    event Action<bool>? BuildingChanged;

    /// <summary>Whether a build is currently running.</summary>
    bool Building { get; }

    /// <summary>Builds the current project for the default configuration; false on failure.</summary>
    Task<bool> BuildAsync(CancellationToken ct);

    /// <summary>The build configuration used by <see cref="BuildAsync"/>.</summary>
    string BuildConfiguration { get; }

    /// <summary>Locates the launcher executable a completed build produced, or null if absent.</summary>
    string? FindProjectLauncher(string buildDirectory);
}
