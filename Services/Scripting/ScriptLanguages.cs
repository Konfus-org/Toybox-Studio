using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Toybox.Studio.Services.Scripting;

/// <summary>
/// The table of languages the script editor can open, and the one place to add another. To support a new
/// language, add a <see cref="ScriptLanguage"/> to <see cref="All"/> with its Monaco id, label, and extensions
/// (plus a language-server name if one backs it); everything else — the per-document language, the status bar,
/// and which ids the page's LSP client serves — is derived from this table. A language whose grammar isn't in
/// the vendored Monaco bundle must also be registered on the page in <c>app/languages.js</c> (as GLSL is).
/// </summary>
public static class ScriptLanguages
{
    /// <summary>C++ scripts and their headers, backed by clangd for engine-aware IntelliSense.</summary>
    public static readonly ScriptLanguage Cpp = new(
        Id: "cpp",
        DisplayName: "C++",
        Extensions: [".cpp", ".cxx", ".cc", ".c", ".h", ".hpp", ".hxx", ".hh", ".inl"],
        LanguageServer: "clangd");

    /// <summary>GLSL shaders — highlight-only (no language server). Grammar registered in <c>app/languages.js</c>.</summary>
    public static readonly ScriptLanguage Glsl = new(
        Id: "glsl",
        DisplayName: "GLSL",
        Extensions: [".glsl", ".frag", ".vert", ".comp", ".geo"]);

    /// <summary>JSON data files (materials, settings, asset metadata) opened for a quick edit.</summary>
    public static readonly ScriptLanguage Json = new(
        Id: "json",
        DisplayName: "JSON",
        Extensions: [".json"]);

    /// <summary>The fallback for any unrecognised extension: shown verbatim with no highlighting.</summary>
    public static readonly ScriptLanguage PlainText = new(
        Id: "plaintext",
        DisplayName: "Text",
        Extensions: []);

    /// <summary>Every known language. Add an entry here to teach the editor a new one.</summary>
    public static readonly IReadOnlyList<ScriptLanguage> All = [Cpp, Glsl, Json];

    // Extension -> language, built once from All. A duplicate extension across two entries is a programming
    // error in the table above, so let the dictionary throw rather than silently picking one.
    private static readonly IReadOnlyDictionary<string, ScriptLanguage> ByExtension =
        All.SelectMany(language => language.Extensions.Select(ext => (ext, language)))
           .ToDictionary(pair => pair.ext, pair => pair.language, StringComparer.OrdinalIgnoreCase);

    /// <summary>The Monaco ids of languages that a language server backs — the set the page registers providers for.</summary>
    public static readonly IReadOnlyList<string> LanguageServerIds =
        All.Where(language => language.UsesLanguageServer).Select(language => language.Id).ToList();

    /// <summary>The language for a file path, by its extension; <see cref="PlainText"/> when unrecognised.</summary>
    public static ScriptLanguage ForPath(string path) =>
        ByExtension.TryGetValue(Path.GetExtension(path), out var language) ? language : PlainText;
}
