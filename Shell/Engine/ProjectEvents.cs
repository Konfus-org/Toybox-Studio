namespace Toybox.Studio.Shell;

// The project layer's event structs, in one place (the engine layer's live in EngineApi/EngineEvents.cs).

/// <summary>The resolved engine source path changed (null = not located).</summary>
public readonly record struct EngineLocated(string? SourcePath);

/// <summary>The open project changed — a relaunch-worthy change.</summary>
public readonly record struct ProjectChanged;
