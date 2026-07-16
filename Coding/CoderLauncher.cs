using Toybox.Studio.Docking;

namespace Toybox.Studio.Coding;

/// <summary>
/// The open-into-Coder seam. Opening a source file routes here (from the asset-open router or File ▸ Open
/// Source): it expands a C++ file into its header + implementation pair and either loads them into the already
/// open Coder panel (bringing it forward) or spawns the panel — whose <see cref="CoderPanelViewModel"/> claims
/// the pending files on construction. A shader or JSON file opens as just itself. Mirrors
/// <c>AssetViewerLauncher</c>, but the Coder panel is a singleton, so an open focuses the one panel rather than
/// spawning duplicates.
/// </summary>
public sealed class CoderLauncher(WorkspaceViewModel workspace)
{
    private IReadOnlyList<string>? _pending;
    private (string File, int Line)? _pendingReveal;
    private Action<string, int>? _showCurrent;

    /// <summary>Opens the source file (and its C++ companion) in Coder: into the open panel when one is up,
    /// else a freshly spawned one. A <paramref name="line"/> above 0 scrolls the file to that 1-based line.</summary>
    public void OpenScript(string absolutePath, int line = 0)
    {
        var companions = Companions(absolutePath);

        // Load into the existing panel only when one is genuinely still open: Focus returns false once the
        // panel has closed (its view-model dropped its show delegate on dispose), so a stale delegate can't
        // swallow the open into a disposed panel — fall through and spawn a fresh one.
        if (_showCurrent is { } show && workspace.Focus<CoderPanelViewModel>())
        {
            foreach (var file in companions)
                show(file, 0);
            // Reveal the clicked file's line last, so it lands active and scrolled to the line (a C++ companion
            // set otherwise leaves the .cpp active).
            if (line > 0)
                show(absolutePath, line);
            return;
        }

        _pending = companions;
        _pendingReveal = line > 0 ? (absolutePath, line) : null;
        workspace.Open<CoderPanelViewModel>();
        _pending = null;
        _pendingReveal = null;
    }

    /// <summary>The file(s) a just-spawned panel should open (claimed once), or an empty list.</summary>
    public IReadOnlyList<string> TakePending()
    {
        var pending = _pending ?? [];
        _pending = null;
        return pending;
    }

    /// <summary>The (file, 1-based line) a just-spawned panel should scroll to after opening its pending files,
    /// or null when the open addressed no specific line. Claimed once.</summary>
    public (string File, int Line)? TakePendingReveal()
    {
        var reveal = _pendingReveal;
        _pendingReveal = null;
        return reveal;
    }

    /// <summary>The panel registers its editor's <c>Open</c> as the current open target.</summary>
    public void RegisterCurrent(Action<string, int> show) => _showCurrent = show;

    /// <summary>The panel drops itself as the open target on close (only if it is still the current one).</summary>
    public void UnregisterCurrent(Action<string, int> show)
    {
        if (_showCurrent == show)
            _showCurrent = null;
    }

    // The files to open for a source path: a C++ file brings its header + implementation pair (header first,
    // .cpp last so it lands active); a shader or anything else opens as just itself.
    private static IReadOnlyList<string> Companions(string sourcePath)
    {
        var directory = Path.GetDirectoryName(sourcePath);
        var stem = Path.GetFileNameWithoutExtension(sourcePath);
        if (directory is null || stem.Length == 0 || ScriptLanguages.ForPath(sourcePath) != ScriptLanguages.Cpp)
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
}
