namespace Toybox.Studio.Coding.Languages;

/// <summary>
/// A running language-server backend for the code editor, bridged to one Monaco surface and tagged by
/// <see cref="Id"/> so several servers (clangd for C++, Roslyn for C#, …) can drive the same page at once.
/// Created by an <see cref="ILanguageServerFactory"/>; disposing it tears the backing process down. The
/// per-language wiring (which Monaco ids it serves, how its process is launched, any post-initialise handshake)
/// lives in the factory, so adding a language never touches the editor or the other languages.
/// </summary>
public interface ILanguageServer : IDisposable
{
    /// <summary>The wire tag that routes LSP traffic to this server on the page (e.g. <c>clangd</c>, <c>roslyn</c>).</summary>
    string Id { get; }

    /// <summary>The Monaco language ids this server provides IntelliSense for (e.g. <c>cpp</c>, <c>csharp</c>).</summary>
    IReadOnlyList<string> LanguageIds { get; }
}
