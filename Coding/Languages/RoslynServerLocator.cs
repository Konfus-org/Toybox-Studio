namespace Toybox.Studio.Coding.Languages;

/// <summary>
/// Finds the Roslyn language server DLL (<c>Microsoft.CodeAnalysis.LanguageServer.dll</c>). Primary source is
/// the copy vendored into the app output under <c>Roslyn/</c> by the build's <c>dotnet tool install</c> step;
/// as a developer fallback it accepts the server that ships with an installed VS Code C# extension. Returns null
/// when neither is present, in which case C# stays highlight-only.
/// </summary>
public static class RoslynServerLocator
{
    private const string ServerDll = "Microsoft.CodeAnalysis.LanguageServer.dll";

    /// <summary>The absolute path to the server DLL, or null when it can't be found.</summary>
    public static string? Locate() => FromVendored() ?? FromVsCodeExtension();

    // The build vendors the tool under <output>/Roslyn via `dotnet tool install --tool-path`; the DLL sits a few
    // levels deep in that self-contained layout, so search the whole subtree.
    private static string? FromVendored()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Roslyn");
        return FirstUnder(root);
    }

    // Developer fallback: the ms-dotnettools.csharp extension bundles the same server under .roslyn/. Pick the
    // newest installed extension version.
    private static string? FromVsCodeExtension()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var extensions = Path.Combine(home, ".vscode", "extensions");
        if (!Directory.Exists(extensions))
            return null;

        string[] candidates;
        try
        {
            candidates = Directory.GetDirectories(extensions, "ms-dotnettools.csharp-*");
        }
        catch (Exception)
        {
            return null;
        }

        foreach (var extension in candidates.OrderByDescending(directory => directory, StringComparer.OrdinalIgnoreCase))
        {
            var dll = Path.Combine(extension, ".roslyn", ServerDll);
            if (File.Exists(dll))
                return dll;
        }

        return null;
    }

    private static string? FirstUnder(string root)
    {
        if (!Directory.Exists(root))
            return null;

        try
        {
            return Directory.EnumerateFiles(root, ServerDll, SearchOption.AllDirectories).FirstOrDefault();
        }
        catch (Exception)
        {
            return null;
        }
    }
}
