using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Windows.Input;
using Toybox.Studio.EngineApi.Types.Worlds;
using Toybox.Studio.Utils;

namespace Toybox.Studio.WorldTree;

/// <summary>
/// One row in the world tree, wrapping a live <see cref="Entity"/> mirror: its name, enabled/global flags
/// (for the row's look), its child rows, and the inline-rename state the tree drives on F2. It tracks the
/// entity's <see cref="Entity.Changed"/> so an engine-applied or gizmo-driven change (a rename streamed
/// back, a disable) refreshes the row without a full tree rebuild. Renaming commits straight onto the
/// synced <see cref="Entity.Name"/> setter, which pushes to the engine. The panel rebuilds (and disposes)
/// these whenever the entity set changes, so a node's lifetime is one registry generation.
/// </summary>
public sealed partial class WorldEntityViewModel : ObservableObject, IWorldMenuTarget, IDisposable
{
    private readonly Entity _entity;

    public WorldEntityViewModel(Entity entity)
    {
        _entity = entity;
        _entity.Changed += OnEntityChanged;
        BeginRenameCommand = new RelayCommand(BeginRename);
        CommitRenameCommand = new RelayCommand(CommitRename);
        CancelRenameCommand = new RelayCommand(CancelRename);
    }

    /// <summary>Starts inline rename (double-tap on the name, or F2). </summary>
    public ICommand BeginRenameCommand { get; }

    /// <summary>Commits the inline rename (Enter, or the box losing focus).</summary>
    public ICommand CommitRenameCommand { get; }

    /// <summary>Abandons the inline rename (Escape).</summary>
    public ICommand CancelRenameCommand { get; }

    /// <summary>The wrapped entity's id — the selection key.</summary>
    public ulong Id => _entity.Id;

    /// <summary>This row's entity, as the world context menu's target (a tree surface).</summary>
    ulong? IWorldMenuTarget.EntityId => _entity.Id;

    WorldMenuSurface IWorldMenuTarget.Surface => WorldMenuSurface.Tree;

    /// <summary>The wrapped entity handle (the inspector and ops resolve against the live registry by id;
    /// this is the row's own snapshot).</summary>
    public Entity Entity => _entity;

    /// <summary>The child rows, in sibling order.</summary>
    public ObservableCollection<WorldEntityViewModel> Children { get; } = [];

    /// <summary>The parent row, or null at a bucket root — set while the tree is built. Drives ancestor
    /// reveal, sibling-index computation for a drag, and the effective-enabled cascade.</summary>
    public WorldEntityViewModel? Parent { get; set; }

    /// <summary>The entity's display name (refreshed on an engine-applied change).</summary>
    public string Name => _entity.Name;

    /// <summary>Whether the entity is enabled — a disabled row draws dimmed.</summary>
    public bool IsEnabled => _entity.IsEnabled;

    /// <summary>Whether the entity is enabled and every ancestor is too — a row dims when it or any ancestor
    /// is disabled, so a disabled subtree reads as inactive.</summary>
    public bool IsEffectivelyEnabled => IsEnabled && (Parent?.IsEffectivelyEnabled ?? true);

    /// <summary>Whether the entity is a world-global (resident) rather than a streamed one — the row shows
    /// a globe.</summary>
    public bool IsGlobal => _entity.IsGlobal;

    /// <summary>Whether this row is the/one of the selected entities (drives the row highlight).</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>Whether the row is expanded to show its children.</summary>
    [ObservableProperty]
    public partial bool IsExpanded { get; set; } = true;

    /// <summary>Whether the row is in inline-rename mode (F2 / double-click), showing an edit box.</summary>
    [ObservableProperty]
    public partial bool IsEditing { get; set; }

    /// <summary>The editable name buffer while <see cref="IsEditing"/>; committed onto the entity.</summary>
    [ObservableProperty]
    public partial string EditingName { get; set; } = "";

    /// <summary>Drops the row into inline rename, seeding the buffer with the current name.</summary>
    public void BeginRename()
    {
        EditingName = Name;
        IsEditing = true;
    }

    /// <summary>Commits the inline rename onto the synced <see cref="Entity.Name"/> (which pushes to the
    /// engine) when it changed to a non-empty value, then leaves edit mode.</summary>
    public void CommitRename()
    {
        if (!IsEditing)
            return;

        var name = EditingName?.Trim() ?? "";
        if (name.Length > 0 && name != _entity.Name)
            _entity.Name = name;
        IsEditing = false;
    }

    /// <summary>Abandons the inline rename, leaving the name unchanged.</summary>
    public void CancelRename() => IsEditing = false;

    public void Dispose()
    {
        _entity.Changed -= OnEntityChanged;
        foreach (var child in Children)
            child.Dispose();
    }

    // An engine-applied or local change to the entity: refresh the row's derived text/flags. Marshalled to
    // the UI thread — a local edit raises on the setter's thread.
    private void OnEntityChanged() => Dispatch.To(DispatchContext.UI, () =>
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(IsEnabled));
        OnPropertyChanged(nameof(IsGlobal));
        NotifyEffectiveEnabledChanged();
    });

    // A change to this row's own enabled flag cascades to its whole subtree: every descendant's effective
    // state depends on this one, so re-raise it down the tree.
    private void NotifyEffectiveEnabledChanged()
    {
        OnPropertyChanged(nameof(IsEffectivelyEnabled));
        foreach (var child in Children)
            child.NotifyEffectiveEnabledChanged();
    }
}
