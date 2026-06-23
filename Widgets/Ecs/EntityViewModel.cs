using Toybox.Studio.Utils;
using Toybox.Studio.Utils.Extensions;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Toybox.Studio.Services.Dialogs;
using Toybox.Studio.Services.World;

namespace Toybox.Studio.Widgets.Ecs;

/// <summary>
/// A persistent, observable view of one entity, keyed by <see cref="Id"/>. The same instance survives world
/// refreshes — <see cref="UpdateFrom"/> reconciles it against the latest snapshot in place — so a selection
/// held against it stays valid without a re-find/reselect. The inspector splits the view: ordinary
/// <see cref="Components"/> on one tab, the script container's scripts on another, and (in debug) the raw
/// component JSON on a third.
/// </summary>
public sealed partial class EntityViewModel : ObservableObject
{
    // The component that holds the entity's scripts; surfaced on its own inspector tab rather than mixed
    // in with ordinary components.
    private const string ScriptComponentName = "script_container";

    // Pinned to the top of the Components tab — it's the component you reach for most often.
    private const string TransformComponentName = "transform";

    private readonly Entity _entity;
    private readonly Func<Task> _resync;
    private readonly Action _onEdited;

    // Guards the enable toggle while UpdateFrom seeds it from a snapshot, so reconciling to engine truth
    // doesn't echo back a redundant entity.setEnabled.
    private bool _suppressEnabledCommit;

    private bool _isEnabled = true;

    public EntityViewModel(Entity entity, Func<Task> resync, Action onEdited)
    {
        _entity = entity;
        _resync = resync;
        _onEdited = onEdited;
    }

    public ulong Id => _entity.Id;

    [ObservableProperty]
    public partial string Name { get; private set; } = "";

    [ObservableProperty]
    public partial IReadOnlyList<string> Tags { get; private set; } = [];

    [ObservableProperty]
    public partial string Subtitle { get; private set; } = "";

    /// <summary>True when this entity is global (a full-lifetime resident); the world view shows it in the
    /// Globals section rather than the world tree.</summary>
    [ObservableProperty]
    public partial bool IsGlobal { get; private set; }

    /// <summary>
    /// Whether the entity is enabled. Flipping it turns the entity off wholesale in the engine — every
    /// runtime system skips a disabled entity — via <c>entity.setEnabled</c>; it stays in the world tree so
    /// it can be turned back on. Seeded from each snapshot without re-committing.
    /// </summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (!SetProperty(ref _isEnabled, value))
                return;

            // The own flag drives this entity's effective state and that of its whole subtree (a disabled
            // parent dims its children), so cascade the dim notification down regardless of whether this is
            // a user toggle or a snapshot seed.
            NotifyEffectiveEnabledChanged();

            if (_suppressEnabledCommit)
                return;

            CommitEnabledAsync(value).FireAndForget();
        }
    }

    /// <summary>
    /// Whether this entity is effectively enabled in the tree: its own <see cref="IsEnabled"/> flag AND
    /// every ancestor's. A disabled parent dims its whole subtree — matching the engine, which skips a
    /// disabled ancestor's descendants in every runtime query — without changing each child's own flag, so
    /// the children light back up the moment the parent is re-enabled.
    /// </summary>
    public bool IsEffectivelyEnabled => IsEnabled && (Parent?.IsEffectivelyEnabled ?? true);

    /// <summary>True while the entity's name is being edited inline in the world list.</summary>
    [ObservableProperty]
    public partial bool IsRenaming { get; set; }

    /// <summary>The in-progress rename text; committed to the engine on Enter / focus loss.</summary>
    [ObservableProperty]
    public partial string RenameDraft { get; set; } = "";

    /// <summary>Ordinary components (everything except the script container) — the "Components" tab.</summary>
    public ObservableCollection<ComponentViewModel> Components { get; } = [];

    /// <summary>True when the entity has at least one ordinary component; drives the Components tab's
    /// empty-state ghost. Re-raised by <see cref="UpdateFrom"/> after the collection is rebuilt.</summary>
    public bool HasComponents => Components.Count > 0;

    /// <summary>The script container, or null when the entity has none — the "Scripts" tab. Presented as
    /// per-script cards (each binding's overrides as ordinary fields) rather than one generic grid.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasScripts))]
    public partial ScriptContainerViewModel? Scripts { get; private set; }

    public bool HasScripts => Scripts is not null;

    /// <summary>Pretty-printed raw component JSON for the debug "Json" tab; <see cref="JsonDraft"/> is edited.</summary>
    [ObservableProperty]
    public partial string Json { get; private set; } = "";

    [ObservableProperty]
    public partial string JsonDraft { get; set; } = "";

    /// <summary>True only in a debug build, gating the inspector's raw-JSON tab.</summary>
    public static bool IsDebug =>
#if DEBUG
        true;
#else
        false;
#endif

    /// <summary>
    /// Whether this node is expanded in the world tree. Held on the VM (not just the TreeViewItem) so it
    /// survives a reconcile and can be set programmatically — selecting a nested entity (e.g. from the
    /// viewport) expands its ancestors to reveal it. The tree binds the container's IsExpanded to this.
    /// </summary>
    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    public ObservableCollection<EntityViewModel> Children { get; } = [];

    /// <summary>
    /// The owning entity in the world tree, or null for a root. Maintained by the world reconcile so the
    /// tree's drag-and-drop can resolve an entity's siblings and parent id when computing a move.
    /// </summary>
    public EntityViewModel? Parent { get; set; }

    /// <summary>Enters inline-rename mode, seeding the draft with the current name.</summary>
    [RelayCommand]
    public void BeginRename()
    {
        RenameDraft = Name;
        IsRenaming = true;
    }

    /// <summary>Reconciles this VM against a fresh snapshot (same id), rebuilding its components.</summary>
    public void UpdateFrom(EntityDescription data)
    {
        Name = data.Name;
        Tags = data.Tags;
        IsGlobal = data.IsGlobal;
        // Seed the enable flag from engine truth without echoing a setEnabled back.
        _suppressEnabledCommit = true;
        IsEnabled = data.IsEnabled;
        _suppressEnabledCommit = false;
        // Tags are intentionally left out of the header for now; just the id, shown right-aligned.
        Subtitle = $"#{data.Id}";

        Components.Clear();
        ScriptContainerViewModel? scripts = null;
        var rawComponents = new JObject();
        // Transform sorts to the top; everything else keeps the engine's registration order (OrderBy is stable).
        var ordered = data.Components.OrderBy(component =>
            component.Name == TransformComponentName ? 0 : 1);
        foreach (var component in ordered)
        {
            rawComponents[component.Name] = component.Raw;
            if (component.Name == ScriptComponentName)
                scripts = new ScriptContainerViewModel(
                    _entity.Component(component.Name), component, _resync, _onEdited);
            else
                Components.Add(new ComponentViewModel(
                    _entity.Component(component.Name), component, _resync, _onEdited));
        }

        Scripts = scripts;
        OnPropertyChanged(nameof(HasComponents));
        Json = rawComponents.ToString(Formatting.Indented);
        JsonDraft = Json;
    }

    /// <summary>
    /// Pushes a fresh snapshot of the same entity into the existing component grids in place, without
    /// rebuilding them — used to track a running game's live values so the inspector doesn't tear down and
    /// recreate its controls (a visible hitch) on every sync tick. Returns false when the entity's shape
    /// changed (a component or the script container was added/removed/retyped, or an array changed length),
    /// in which case the caller should fall back to <see cref="UpdateFrom"/> for a full rebuild.
    /// Scripts and the raw-JSON tab are deliberately left untouched: script overrides are authored values,
    /// not live game state, and re-serializing the JSON each tick would clobber an in-progress edit.
    /// </summary>
    public bool TrySyncValues(EntityDescription data)
    {
        // Same transform-first ordering as UpdateFrom (OrderBy is stable), so the rows zip one to one.
        var ordered = data.Components
            .OrderBy(component => component.Name == TransformComponentName ? 0 : 1)
            .ToList();

        var hasScripts = ordered.Any(component => component.Name == ScriptComponentName);
        if (hasScripts != (Scripts is not null))
            return false;

        var ordinary = ordered.Where(component => component.Name != ScriptComponentName).ToList();
        if (ordinary.Count != Components.Count)
            return false;

        var synced = true;
        for (var index = 0; index < Components.Count; index++)
        {
            if (!string.Equals(Components[index].Name, ordinary[index].Name, StringComparison.Ordinal))
                return false;
            synced &= Components[index].SyncFrom(ordinary[index]);
        }

        return synced;
    }

    /// <summary>
    /// Re-queries each component's per-property modified (set vs default) state. Called when this entity is
    /// the inspector's selection, so the indicator stays accurate without polling every entity in the world.
    /// </summary>
    public void RefreshModifiedState()
    {
        foreach (var component in Components)
            component.RefreshModifiedAsync().FireAndForget();
        // Script overrides carry their set/default state intrinsically (an override is a set value), so the
        // script container needs no per-property reflect.isDefault round-trip.
    }

    /// <summary>Replaces the child VMs (the persistent instances are reused by the world reconcile).</summary>
    public void SetChildren(IReadOnlyList<EntityViewModel> children)
    {
        foreach (var child in children)
            child.Parent = this;
        // Reconcile in place so an unchanged subtree keeps its expansion/selection and only real changes move.
        Children.Reconcile(children);
        // Re-parenting can change a child's effective-enabled (this node may be disabled), so refresh the dim
        // of each child's subtree now its ancestor chain is settled.
        foreach (var child in children)
            child.NotifyEffectiveEnabledChanged();
    }

    // Re-raises IsEffectivelyEnabled for this node and every descendant: a node's effective state depends on
    // its ancestors, so a change at one level must refresh the dimming of the whole subtree below it.
    internal void NotifyEffectiveEnabledChanged()
    {
        OnPropertyChanged(nameof(IsEffectivelyEnabled));
        foreach (var child in Children)
            child.NotifyEffectiveEnabledChanged();
    }

    // Reflects a drag between the streamed and globals sections locally (the optimistic move), ahead of the
    // engine round-trip confirming it. The IsGlobal setter is otherwise driven only by UpdateFrom.
    internal void SetGlobalLocal(bool global) => IsGlobal = global;

    private async Task CommitEnabledAsync(bool enabled)
    {
        var result = await _entity.SetEnabledAsync(enabled, CancellationToken.None)
            .ContinueOnSameContext();
        if (result.Success)
        {
            _onEdited();
            return;
        }

        await Popups.ShowErrorAsync("Couldn't change entity state", result.Error!).ContinueOnSameContext();
        await _resync().ContinueOnSameContext();
    }

    /// <summary>Leaves inline-rename mode without applying the draft.</summary>
    [RelayCommand]
    private void CancelRename() => IsRenaming = false;

    /// <summary>Commits the inline-rename draft to the engine (no-op if blank/unchanged), then leaves edit
    /// mode. The name is updated in place on success so the row refreshes without a tree rebuild.</summary>
    [RelayCommand]
    private async Task CommitRenameAsync()
    {
        if (!IsRenaming)
            return;
        IsRenaming = false;

        var name = RenameDraft.Trim();
        if (name.Length == 0 || name == Name)
            return;

        var result = await _entity.RenameAsync(name, CancellationToken.None)
            .ContinueOnSameContext();
        if (result.Success)
        {
            Name = name;
            _onEdited();
        }
        else
            await Popups.ShowErrorAsync("Couldn't rename entity", result.Error!).ContinueOnSameContext();
    }

    /// <summary>
    /// Pushes the edited raw JSON back to the engine, one component at a time via entity.setComponent, then
    /// re-syncs to engine truth. Surfaces a popup on invalid JSON or a rejected component.
    /// </summary>
    [RelayCommand]
    private async Task ApplyJsonAsync()
    {
        JObject parsed;
        try
        {
            parsed = JObject.Parse(JsonDraft);
        }
        catch (JsonException exception)
        {
            await Popups.ShowErrorAsync("Invalid JSON", exception.Message).ContinueOnSameContext();
            return;
        }

        var applied = false;
        foreach (var component in parsed.Properties())
        {
            if (component.Value is not JObject value)
                continue;

            var result = await _entity.Component(component.Name)
                .SetAsync(value, CancellationToken.None)
                .ContinueOnSameContext();
            if (!result.Success)
            {
                await Popups.ShowErrorAsync(
                    "Couldn't apply JSON",
                    $"The engine rejected component '{component.Name}':\n\n{result.Error}")
                    .ContinueOnSameContext();
                break;
            }

            applied = true;
        }

        if (applied)
            _onEdited();

        await _resync().ContinueOnSameContext();
    }
}
