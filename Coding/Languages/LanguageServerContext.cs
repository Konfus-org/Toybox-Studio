using Toybox.Studio.Logging;

namespace Toybox.Studio.Coding.Languages;

/// <summary>
/// The project/environment facts a language-server factory needs to launch and point its server at the right
/// sources, gathered once by the editor. A factory takes only what it needs — clangd uses the project and
/// engine roots (for <c>.clangd</c> + <c>compile_commands.json</c>), Roslyn uses the studio root (its own
/// solution) — so no factory reaches back into editor state.
/// </summary>
/// <param name="ProjectRoot">The open project's root directory (the C++ script project).</param>
/// <param name="EngineRoot">The located engine checkout, or null when it couldn't be found.</param>
/// <param name="StudioRoot">The Studio's own source root (holds <c>Toybox.Studio.slnx</c>) for C# IntelliSense.</param>
/// <param name="Log">The unified log; a factory surfaces its start/attach outcome here.</param>
public sealed record LanguageServerContext(
    string ProjectRoot,
    string? EngineRoot,
    string StudioRoot,
    Logger Log);
