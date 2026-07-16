using Toybox.Studio.EngineApi.Types.Assets;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Keybindings;
using Icon = IconPacks.Avalonia.Lucide.PackIconLucideKind;

namespace Toybox.Studio.WorldTree;

/// <summary>
/// Registers the World Tree's entity edit actions — copy/cut/paste, duplicate, delete, rename — scoped to
/// the panel via <see cref="Scheme.For{TViewModel}"/> over <see cref="WorldTreeViewModel"/>, so their
/// chords (Ctrl+C/X/V, Ctrl+D, Del, F2) fire only while the hierarchy has focus. The panel executes them
/// (handling <see cref="EditorActionInvoked"/> through <see cref="EntityOperations"/>); this is the single
/// place their bindings and default chords are declared. Constructed once by the composition root before
/// the keymap builds.
/// </summary>
public sealed class WorldTreeActions
{
    public WorldTreeActions(ActionRegistry actions)
    {
        var scheme = Scheme.For<WorldTreeViewModel>();

        actions.Register(new EditorAction
        {
            Id = ActionIds.EntityCopy,
            Title = "Copy Entity",
            Category = "Entity",
            Icon = Icon.Copy,
            Scheme = scheme,
            DefaultChords = [Chord(InputKey.C, ctrl: true)],
        });
        actions.Register(new EditorAction
        {
            Id = ActionIds.EntityCut,
            Title = "Cut Entity",
            Category = "Entity",
            Icon = Icon.Scissors,
            Scheme = scheme,
            DefaultChords = [Chord(InputKey.X, ctrl: true)],
        });
        actions.Register(new EditorAction
        {
            Id = ActionIds.EntityPaste,
            Title = "Paste Entity",
            Category = "Entity",
            Icon = Icon.ClipboardPaste,
            Scheme = scheme,
            DefaultChords = [Chord(InputKey.V, ctrl: true)],
        });
        actions.Register(new EditorAction
        {
            Id = ActionIds.EntityDuplicate,
            Title = "Duplicate Entity",
            Category = "Entity",
            Icon = Icon.CopyPlus,
            Scheme = scheme,
            DefaultChords = [Chord(InputKey.D, ctrl: true)],
        });
        actions.Register(new EditorAction
        {
            Id = ActionIds.EntityRename,
            Title = "Rename Entity",
            Category = "Entity",
            Icon = Icon.Pencil,
            Scheme = scheme,
            DefaultChords = [Chord(InputKey.F2)],
        });
        actions.Register(new EditorAction
        {
            Id = ActionIds.EntityDelete,
            Title = "Delete Entity",
            Category = "Entity",
            Icon = Icon.Trash2,
            Scheme = scheme,
            DefaultChords = [Chord(InputKey.Delete)],
        });
    }

    private static KeyChordInputControl Chord(InputKey key, bool ctrl = false) =>
        new() { Key = key, Ctrl = ctrl };
}
