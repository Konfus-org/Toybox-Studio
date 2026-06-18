using System.Collections.ObjectModel;
using System.Linq;
using Newtonsoft.Json.Linq;
using Toybox.Studio.Services.EngineApi;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// The base-aware editor for a <c>MaterialInstance</c> wherever it appears — a nested field or a list element
/// (the top-level <c>material_instance</c> component reuses <see cref="Build"/> directly and flattens the pair
/// into its own rows, see ComponentViewModel). It pairs a "Base" material picker with a
/// <see cref="MaterialOverridesViewModel"/> that shows the referenced material's slots; changing the base
/// reloads those slots. As a composite row it renders its own expandable header above the two child editors.
/// </summary>
public sealed class MaterialInstancePropertyViewModel : PropertyViewModel
{
    /// <summary>The engine type token a nested/list <c>MaterialInstance</c> node carries (its snake_case type name).</summary>
    public const string TypeToken = "material_instance";

    private const string MaterialField = "material";
    private const string OverridesField = "overrides";

    public MaterialInstancePropertyViewModel(PropertyNode node, EngineRpc engine, Action? commit, int depth)
        : base(node)
    {
        // A nested instance has no per-field reflect.set granularity: every edit re-commits the one top-level
        // property that contains it, so the base picker and the overrides share the single commit.
        var (material, overrides) = Build(
            FindField(node, MaterialField)!,
            FindField(node, OverridesField)!,
            engine,
            () => ReadHandleId(node.Value?[MaterialField]),
            commit,
            commit,
            depth + 1);
        Children = [material, overrides];
    }

    public override bool IsComposite => true;

    public override bool HasChildren => true;

    public ObservableCollection<PropertyViewModel> Children { get; }

    // Nested items default collapsed (the user opens the ones they care about), matching ObjectPropertyViewModel.
    private bool _isExpanded;

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    protected override IEnumerable<PropertyViewModel> FilterChildren => Children;

    /// <summary>
    /// True when <paramref name="node"/> is shaped as a material instance the editor can build (it carries the
    /// "material" handle and "overrides" sub-struct). The factory falls back to the generic grid otherwise.
    /// </summary>
    public static bool CanBuild(PropertyNode node) =>
        FindField(node, MaterialField) is not null && FindField(node, OverridesField) is not null;

    /// <summary>
    /// Builds the Base material picker and its override editor, wired so picking a new base reloads the
    /// override slots against it. The picker and overrides commit independently: the top-level component grid
    /// routes each to its own <c>reflect.set</c> property, while a nested instance passes the same commit for
    /// both (one round-trip re-sends the whole containing property). <paramref name="readMaterialId"/> reads
    /// the base material's id live from the backing JSON each time, since the picker replaces (not mutates) the
    /// handle token on a pick. Shared by the composite VM here and ComponentViewModel.
    /// </summary>
    public static (PropertyViewModel Material, MaterialOverridesViewModel Overrides) Build(
        PropertyNode materialNode,
        PropertyNode overridesNode,
        EngineRpc engine,
        Func<long> readMaterialId,
        Action? commitMaterial,
        Action? commitOverrides,
        int depth)
    {
        var overrides =
            new MaterialOverridesViewModel(overridesNode, readMaterialId(), engine, commitOverrides, depth);

        var material = PropertyViewModelFactory.Create(
            materialNode,
            () =>
            {
                commitMaterial?.Invoke();
                overrides.ReloadBaseAsync(readMaterialId()).FireAndForget();
            },
            depth);

        return (material, overrides);
    }

    /// <summary>
    /// Reads a handle field's asset id out of the live component/instance JSON, accepting both a bare id and a
    /// <c>{ …, value: &lt;id&gt; }</c> wrapper. Read live (not cached) so it reflects the current base after a pick.
    /// </summary>
    public static long ReadHandleId(JToken? handleField) =>
        (handleField is JObject wrapper && wrapper.TryGetValue("value", out var value) ? value : handleField)
        ?.Value<long>() ?? 0;

    private static PropertyNode? FindField(PropertyNode node, string name) =>
        node.Children.FirstOrDefault(child => child.Name == name);
}
