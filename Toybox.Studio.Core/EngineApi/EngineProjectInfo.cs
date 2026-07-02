namespace Toybox.Studio.EngineApi;

/// <summary>
/// The open project's identity as the engine <see cref="Session"/> needs it to compile and launch — a
/// minimal, core-owned view of the richer project record held by the project layer.
/// </summary>
public sealed record EngineProjectInfo(
    string Name,
    string ModuleName,
    string AppSettingsPath,
    string BuildDirectory);
