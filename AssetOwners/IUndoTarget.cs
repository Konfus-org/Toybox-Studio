namespace Toybox.Studio.AssetOwners;

/// <summary>
/// A focused document that can undo/redo its own edits — what the workspace's active panel exposes so
/// Edit ▸ Undo/Redo and the global undo keybindings act on whichever document currently has focus (see
/// the workspace's Focused item). <see cref="UndoStateChanged"/> fires when
/// <see cref="CanUndo"/>/<see cref="CanRedo"/> may have changed, so a menu can refresh its enabled state.
/// </summary>
public interface IUndoTarget
{
    bool CanUndo { get; }

    bool CanRedo { get; }

    void Undo();

    void Redo();

    event Action? UndoStateChanged;
}
