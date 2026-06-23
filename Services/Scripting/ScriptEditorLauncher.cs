using Toybox.Studio.Shell.Workspace;
using Toybox.Studio.Widgets.ScriptEditor;

namespace Toybox.Studio.Services.Scripting;

/// <summary>
/// Bridges the inspector's "Pop out" action to the dockable script editor: it brings the (singleton) editor
/// window up and tells it to open the given source file as a tab. Kept as its own small service so the inline
/// strip's view-model depends on this seam rather than reaching into the workspace and the editor view-model
/// directly.
/// </summary>
public sealed class ScriptEditorLauncher
{
    private const string EditorDockableId = "ScriptEditor";

    private readonly WorkspaceViewModel _workspace;
    private readonly ScriptEditorViewModel _editor;

    public ScriptEditorLauncher(WorkspaceViewModel workspace, ScriptEditorViewModel editor)
    {
        _workspace = workspace;
        _editor = editor;
    }

    /// <summary>Opens (or focuses) the script editor window and shows <paramref name="absolutePath"/> in a tab.</summary>
    public void Open(string absolutePath)
    {
        _workspace.OpenDockable(EditorDockableId);
        _editor.Open(absolutePath);
    }
}
