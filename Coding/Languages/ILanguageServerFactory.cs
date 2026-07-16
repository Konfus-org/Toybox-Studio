using Toybox.Studio.Monaco;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Coding.Languages;

/// <summary>
/// The single seam for teaching the editor a language server. One implementation per backend (clangd, Roslyn)
/// declares the Monaco languages it serves and knows how to launch its process and wire it to a page. The editor
/// starts a factory lazily, the first time a file of one of its <see cref="LanguageIds"/> is opened, so opening
/// a C++ file never spins the C# server (and vice-versa). Add a language by adding a factory to
/// <see cref="LanguageServers.Factories"/> — nothing else changes.
/// </summary>
public interface ILanguageServerFactory
{
    /// <summary>The Monaco language ids this factory's server backs (matches <see cref="ScriptLanguage.Id"/>).</summary>
    IReadOnlyList<string> LanguageIds { get; }

    /// <summary>
    /// Launches the server for <paramref name="context"/> and wires it to <paramref name="session"/> (spawning
    /// the process, relaying LSP, and calling <see cref="MonacoSession.EnableLsp"/>). Returns the running server,
    /// or <c>Ok(null)</c> when the backend is simply unavailable (e.g. the tool isn't installed) so the editor
    /// falls back to highlight-only; a hard failure returns <c>Fail</c> with a reason.
    /// </summary>
    Result<ILanguageServer?> Start(MonacoSession session, LanguageServerContext context);
}
