using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Toybox.Studio.Dialogs;
using Toybox.Studio.ECS;
using Toybox.Studio.EngineApi;

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

    private readonly EngineRpc _engine;
    private readonly Func<Task> _resync;

    public EntityViewModel(ulong id, EngineRpc engine, Func<Task> resync)
    {
        Id = id;
        _engine = engine;
        _resync = resync;
    }

    public ulong Id { get; }

    [ObservableProperty]
    public partial string Name { get; private set; } = "";

    [ObservableProperty]
    public partial string Tag { get; private set; } = "";

    [ObservableProperty]
    public partial string Subtitle { get; private set; } = "";

    /// <summary>True when this entity is global (a full-lifetime resident); the world view shows it in the
    /// Globals section rather than the scene tree.</summary>
    [ObservableProperty]
    public partial bool IsGlobal { get; private set; }

    /// <summary>Ordinary components (everything except the script container) — the "Components" tab.</summary>
    public ObservableCollection<ComponentViewModel> Components { get; } = [];

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

    public ObservableCollection<EntityViewModel> Children { get; } = [];

    /// <summary>
    /// The owning entity in the world tree, or null for a root. Maintained by the world reconcile so the
    /// tree's drag-and-drop can resolve an entity's siblings and parent id when computing a move.
    /// </summary>
    public EntityViewModel? Parent { get; set; }

    /// <summary>Reconciles this VM against a fresh snapshot (same id), rebuilding its components.</summary>
    public void UpdateFrom(Entity data)
    {
        Name = data.Name;
        Tag = data.Tag;
        IsGlobal = data.IsGlobal;
        // Tag is intentionally left out of the header for now; just the id, shown right-aligned.
        Subtitle = $"#{data.Id}";

        Components.Clear();
        ScriptContainerViewModel? scripts = null;
        var rawComponents = new JObject();
        foreach (var component in data.Components)
        {
            rawComponents[component.Name] = component.Raw;
            if (component.Name == ScriptComponentName)
                scripts = new ScriptContainerViewModel(Id, component, _engine, _resync);
            else
                Components.Add(new ComponentViewModel(Id, component, _engine, _resync));
        }

        Scripts = scripts;
        Json = rawComponents.ToString(Formatting.Indented);
        JsonDraft = Json;
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
        Children.Clear();
        foreach (var child in children)
        {
            child.Parent = this;
            Children.Add(child);
        }
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

        foreach (var component in parsed.Properties())
        {
            if (component.Value is not JObject value)
                continue;

            var result = await _engine
                .SetComponentAsync(Id, component.Name, value, CancellationToken.None)
                .ContinueOnSameContext();
            if (!result.Success)
            {
                await Popups.ShowErrorAsync(
                    "Couldn't apply JSON",
                    $"The engine rejected component '{component.Name}':\n\n{result.Error}")
                    .ContinueOnSameContext();
                break;
            }
        }

        await _resync().ContinueOnSameContext();
    }
}
