namespace Toybox.Studio.Coding.Languages;

/// <summary>
/// The registry of language-server backends the editor can start. Add a language server by adding its factory
/// here; the editor discovers which one (if any) backs a given file's language and starts it lazily. Highlight-
/// only languages (GLSL, JSON) simply have no factory.
/// </summary>
public static class LanguageServers
{
    /// <summary>Every language-server factory, one per backend. Order is not significant.</summary>
    public static readonly IReadOnlyList<ILanguageServerFactory> Factories =
    [
        new ClangdServerFactory(),
        new RoslynServerFactory(),
    ];

    /// <summary>The factory whose server serves <paramref name="languageId"/>, or null when none does.</summary>
    public static ILanguageServerFactory? ForLanguage(string languageId) =>
        Factories.FirstOrDefault(
            factory => factory.LanguageIds.Contains(languageId, StringComparer.OrdinalIgnoreCase));
}
