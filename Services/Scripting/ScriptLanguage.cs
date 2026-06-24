using System.Collections.Generic;

namespace Toybox.Studio.Services.Scripting;

/// <summary>
/// One language the script editor knows how to show: a Monaco language id, the file extensions that select it,
/// a human label for the status bar, and (optionally) the name of a language server that backs it. This is the
/// single unit the editor reasons about instead of hard-coding "cpp" everywhere — see <see cref="ScriptLanguages"/>
/// for the table of known languages and how a path maps to one.
/// </summary>
/// <param name="Id">
/// The Monaco language id (e.g. <c>cpp</c>, <c>glsl</c>, <c>json</c>). Must match a language Monaco knows —
/// either one in the vendored basic-languages bundle or one the page registers itself (see <c>app/languages.js</c>).
/// </param>
/// <param name="DisplayName">The label shown in the editor status bar (e.g. <c>C++</c>, <c>GLSL</c>).</param>
/// <param name="Extensions">
/// Lower-case file extensions (with the leading dot) that resolve to this language. Each must be unique across
/// the whole table.
/// </param>
/// <param name="LanguageServer">
/// The display name of the language server providing IntelliSense for this language (e.g. <c>clangd</c>), or
/// null for highlight-only languages. Drives both the status bar text and which Monaco language ids the page's
/// LSP client registers completion/hover/etc. providers for.
/// </param>
public sealed record ScriptLanguage(
    string Id,
    string DisplayName,
    IReadOnlyList<string> Extensions,
    string? LanguageServer = null)
{
    /// <summary>True when a language server backs this language (so the editor wires LSP for it).</summary>
    public bool UsesLanguageServer => LanguageServer is not null;
}
