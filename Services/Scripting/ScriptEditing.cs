using Toybox.Studio.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Toybox.Studio.Services.Project;
using Toybox.Studio.Widgets.ScriptEditor;

namespace Toybox.Studio.Services.Scripting;

/// <summary>
/// The entry point both editor surfaces share: it maps a bound script (by its class/type name, e.g.
/// <c>PlayerController</c>) to its C++ source file under the open project, builds the inline editor for a
/// binding card, and pops a script out into the dockable window. A binding's <c>script</c> id resolves
/// (through the asset catalog) to the script's display name, which equals its class name — the engine names a
/// script <c>.cpp</c> in <c>snake_case</c> (<c>player_controller.cpp</c>), so the source is found by
/// converting the name and searching the project's <c>Source</c> tree. Lookups are cached per project.
///
/// Constructed deep inside the inspector's binding cards, which are built by a factory with no DI access, so
/// — like <see cref="Toybox.Studio.Widgets.PropertyGrid.PropertyViewRegistry"/> — a single
/// <see cref="Current"/> instance is wired once at startup and read statically by those cards.
/// </summary>
public sealed class ScriptEditing
{
    private readonly ProjectManager _projects;
    private readonly MonacoAssetServer _server;
    private readonly ScriptDocumentService _documents;
    private readonly ScriptEditorLauncher _launcher;

    private readonly Dictionary<string, Result<string>> _sourceByName =
        new(StringComparer.OrdinalIgnoreCase);
    private string? _cacheRoot;

    public ScriptEditing(
        ProjectManager projects,
        MonacoAssetServer server,
        ScriptDocumentService documents,
        ScriptEditorLauncher launcher,
        ScriptHotReload hotReload)
    {
        _projects = projects;
        _server = server;
        _documents = documents;
        _launcher = launcher;
        HotReload = hotReload;
    }

    /// <summary>The instance the inspector's binding cards read; set once after the app's services are built.</summary>
    public static ScriptEditing? Current { get; set; }

    /// <summary>The shared hot-reload toggle the editor surfaces' lightning-bolt controls bind to.</summary>
    public ScriptHotReload HotReload { get; }

    /// <summary>
    /// Resolves a script's class name to its source file under the open project, preferring the
    /// <c>.cpp</c> over the header. Cached per project root; a failure carries why (no project, file missing).
    /// </summary>
    public Result<string> ResolveSource(string scriptName)
    {
        if (_projects.CurrentProject is not { } project)
            return Result<string>.Fail("No project is open.");

        // Drop the cache when the open project changes, so a stale path from a previous project never leaks.
        if (!string.Equals(_cacheRoot, project.RootDirectory, StringComparison.OrdinalIgnoreCase))
        {
            _sourceByName.Clear();
            _cacheRoot = project.RootDirectory;
        }

        if (_sourceByName.TryGetValue(scriptName, out var cached))
            return cached;

        var resolved = Search(project.RootDirectory, scriptName);
        _sourceByName[scriptName] = resolved;
        return resolved;
    }

    /// <summary>
    /// Pops the script out into the dockable editor, opening both its header and implementation as tabs (the
    /// <c>.cpp</c> last, so it lands active). Given either the <c>.cpp</c> or the <c>.h</c>, it finds the
    /// sibling beside it.
    /// </summary>
    public void PopOut(string sourcePath)
    {
        foreach (var companion in Companions(sourcePath))
            _launcher.Open(companion);
    }

    // The header + implementation pair for a source path (header first, .cpp last so it opens active).
    private static IReadOnlyList<string> Companions(string sourcePath)
    {
        var directory = Path.GetDirectoryName(sourcePath);
        var stem = Path.GetFileNameWithoutExtension(sourcePath);
        if (directory is null || stem.Length == 0)
            return [sourcePath];

        var ordered = new List<string>();
        var header = Path.Combine(directory, stem + ".h");
        var implementation = Path.Combine(directory, stem + ".cpp");
        if (File.Exists(header))
            ordered.Add(header);
        if (File.Exists(implementation))
            ordered.Add(implementation);
        return ordered.Count > 0 ? ordered : [sourcePath];
    }

    /// <summary>
    /// Builds the inline editor for a binding card: the loopback page URL plus the shared document buffers for
    /// the script's header and implementation (a C++ script is a pair, so both open). Returns a failure (shown
    /// in the card) when the editor can't run or no source could be opened.
    /// </summary>
    public Result<InlineScriptEditorViewModel> CreateInline(string sourcePath)
    {
        var started = _server.EnsureStarted();
        if (!started)
            return Result<InlineScriptEditorViewModel>.Fail(started.Error!);

        var documents = new List<ScriptDocument>();
        string? lastError = null;
        foreach (var companion in Companions(sourcePath))
        {
            var opened = _documents.GetOrOpen(companion);
            if (opened)
                documents.Add(opened.Value!);
            else
                lastError = opened.Error;
        }

        if (documents.Count == 0)
            return Result<InlineScriptEditorViewModel>.Fail(lastError ?? $"Couldn't open '{sourcePath}'.");

        var page = new Uri(started.Value!, "index.html");
        return Result<InlineScriptEditorViewModel>.Ok(
            new InlineScriptEditorViewModel(page, documents, _documents, HotReload));
    }

    private static Result<string> Search(string root, string scriptName)
    {
        var stem = ToSnakeCase(scriptName);
        // The script lives under the project's source tree; searching Source (not the whole root) keeps the
        // generated/build outputs out and the walk small.
        var searchRoot = Path.Combine(root, "Source");
        if (!Directory.Exists(searchRoot))
            searchRoot = root;

        // Implementation first (that's where logic is edited), then the header as a fallback.
        foreach (var extension in new[] { ".cpp", ".h" })
        {
            string[] matches;
            try
            {
                matches = Directory.GetFiles(searchRoot, stem + extension, SearchOption.AllDirectories);
            }
            catch (Exception e)
            {
                return Result<string>.Fail($"Couldn't search for '{stem}{extension}': {e.Message}");
            }

            if (matches.Length > 0)
                return Result<string>.Ok(matches[0]);
        }

        return Result<string>.Fail($"No source file found for script '{scriptName}' (looked for {stem}.cpp/.h).");
    }

    /// <summary>
    /// Converts a script class name to the engine's source-file stem: <c>PlayerController</c> →
    /// <c>player_controller</c>. An underscore is inserted before an uppercase letter that starts a new word
    /// (preceded by a lowercase letter, or itself followed by a lowercase letter).
    /// </summary>
    private static string ToSnakeCase(string name)
    {
        var builder = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c) && i > 0)
            {
                var prevLower = char.IsLower(name[i - 1]);
                var nextLower = i + 1 < name.Length && char.IsLower(name[i + 1]);
                if (prevLower || nextLower)
                    builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }
}
