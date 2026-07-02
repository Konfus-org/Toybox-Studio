using System.Collections.ObjectModel;
using System.Linq;
using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Project;
using Toybox.Studio.Project.Assets;
using Toybox.Studio.Utils;

namespace Toybox.Studio.PropertyGrid;

/// <summary>
/// The base-aware editor for a <c>MaterialInstance</c> wherever it appears — a nested field or a list element
/// (the top-level <c>material_instance</c> component reuses <see cref="Build"/> directly and flattens the pair
/// into its own rows, see ComponentViewModel). It pairs a "Base" material picker with a
/// <see cref="MaterialOverridesViewModel"/> that shows the referenced material's slots; changing the base
/// reloads those slots. As a composite row it renders its own expandable header above the two child editors.
/// </summary>
public sealed class MaterialInstancePropertyViewModel : PropertyViewModel, IExpandable
{
    /// <summary>The engine type token a nested/list <c>MaterialInstance</c> node carries (its snake_case type name).</summary>
    public const string TypeToken = "material_instance";

    private const string MaterialField = "material";
    private const string OverridesField = "overrides";

    // Nested items default collapsed (the user opens the ones they care about), matching ObjectPropertyViewModel.
    private bool _isExpanded;

    public MaterialInstancePropertyViewModel(PropertyDescriptor descriptor, IValueAccessor accessor, int depth)
        : base(descriptor, accessor)
    {
        var materialDescriptor = FindField(descriptor, MaterialField)!;
        var overridesDescriptor = FindField(descriptor, OverridesField)!;

        // The base picker + override editor are JSON-shaped, so on the TYPED path (a reflected MaterialInstance
        // field — Sky/PostProcessing) they bind to a bare JSON VIEW of the instance's material/overrides and commit
        // the whole instance back through the reflected accessor. On the JSON path (the describe-sourced
        // material_instance component) they bind to the live member tokens directly.
        var (material, overrides) = accessor.Get() is JObject
            ? BuildFromJson(accessor, materialDescriptor, overridesDescriptor, depth + 1)
            : BuildFromTyped(accessor, materialDescriptor, overridesDescriptor, depth + 1);
        Children = [material, overrides];

        // The disclosure chevron renders in the row's reserved leading gutter, like every other composite
        // row, instead of a hand-placed chevron in the value well on the right.
        Disclosure = new DropdownPart(this);
    }

    // The JSON path: the base picker and overrides bind to the live member tokens the parent accessor hands out,
    // and a nested instance re-commits the whole containing property (the same commit for both).
    private static (PropertyViewModel Material, MaterialOverridesViewModel Overrides) BuildFromJson(
        IValueAccessor accessor, PropertyDescriptor materialDescriptor, PropertyDescriptor overridesDescriptor, int depth) =>
        Build(
            (materialDescriptor, accessor.Member(materialDescriptor)),
            (overridesDescriptor, accessor.Member(overridesDescriptor)),
            () => ReadHandleId(accessor.Member(materialDescriptor)),
            accessor.Commit,
            accessor.Commit,
            depth);

    // The typed path: the JSON-shaped editors bind to a bare JSON VIEW of the reflected MaterialInstance's fields.
    // An edit mutates the live instance's Material/Overrides property (read back from the view) then re-commits the
    // whole reflected field through the parent accessor, which reconstructs the field and pushes/dirties.
    private static (PropertyViewModel Material, MaterialOverridesViewModel Overrides) BuildFromTyped(
        IValueAccessor accessor, PropertyDescriptor materialDescriptor, PropertyDescriptor overridesDescriptor, int depth)
    {
        var instance = accessor.Get() as MaterialInstance;
        var materialView = new JsonAccessor(EngineSyncValue.WriteBare(instance?.Material), EngineTypes.Handle, null);
        var overridesView = new JsonAccessor(EngineSyncValue.WriteBare(instance?.Overrides), EngineTypes.Object, null);

        void CommitMaterial()
        {
            if (accessor.Get() is MaterialInstance live && materialView.Get() is JToken token)
                live.Material = EngineSyncValue.ReadBare<AssetHandle>(token);
            accessor.Commit();
        }

        void CommitOverrides()
        {
            if (accessor.Get() is MaterialInstance live && overridesView.Get() is JToken token)
                live.Overrides = EngineSyncValue.ReadBare<MaterialOverrides>(token);
            accessor.Commit();
        }

        return Build(
            (materialDescriptor, materialView),
            (overridesDescriptor, overridesView),
            () => ReadHandleId(materialView),
            CommitMaterial,
            CommitOverrides,
            depth);
    }

    /// <summary>
    /// Bridges a reflected <c>overrides</c> accessor (whose <see cref="IValueAccessor.Get"/> yields a typed
    /// <c>MaterialOverrides</c>) to the JSON-shaped <see cref="MaterialOverridesViewModel"/>: it returns a bare
    /// JSON VIEW of the current overrides plus a commit that reads the (edited) view back into the reflected model
    /// and re-commits it through <paramref name="overrides"/>. Shared by the nested/list typed path
    /// (<see cref="BuildFromTyped"/>) and the top-level typed <c>material_instance</c> component (ComponentViewModel),
    /// so the JSON-view + write-back exists in one place.
    /// </summary>
    public static (JsonAccessor View, Action Commit) TypedOverridesBridge(IValueAccessor overrides)
    {
        var view = new JsonAccessor(EngineSyncValue.WriteBare(overrides.Get()), EngineTypes.Object, null);

        void Commit()
        {
            if (view.Get() is JToken token)
                overrides.Set(EngineSyncValue.ReadBare(overrides.ValueType, token));
            overrides.Commit();
        }

        return (view, Commit);
    }

    public override bool IsComposite => true;

    public override bool HasChildren => true;

    public ObservableCollection<PropertyViewModel> Children { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    protected override IEnumerable<PropertyViewModel> FilterChildren => Children;

    /// <summary>
    /// True when <paramref name="descriptor"/> is shaped as a material instance the editor can build (it carries
    /// the "material" handle and "overrides" sub-struct). The factory falls back to the generic grid otherwise.
    /// </summary>
    public static bool CanBuild(PropertyDescriptor descriptor) =>
        FindField(descriptor, MaterialField) is not null && FindField(descriptor, OverridesField) is not null;

    /// <summary>
    /// Builds the Base material picker and its override editor, wired so picking a new base reloads the
    /// override slots against it. The picker and overrides commit independently: the top-level component grid
    /// routes each to its own <c>reflect.set</c> property, while a nested instance passes the same commit for
    /// both (one round-trip re-sends the whole containing property). <paramref name="readMaterialId"/> reads
    /// the base material's id live from the backing value each time, since the picker replaces the handle on a
    /// pick. Shared by the composite VM here and ComponentViewModel.
    /// </summary>
    public static (PropertyViewModel Material, MaterialOverridesViewModel Overrides) Build(
        (PropertyDescriptor Descriptor, IValueAccessor Accessor) material,
        (PropertyDescriptor Descriptor, IValueAccessor Accessor) overrides,
        Func<ulong> readMaterialId,
        Action? commitMaterial,
        Action? commitOverrides,
        int depth)
    {
        var overridesEditor =
            new MaterialOverridesViewModel(overrides.Descriptor, overrides.Accessor, readMaterialId(), commitOverrides, depth);

        var materialEditor = PropertyViewModelFactory.Create(material.Descriptor, material.Accessor, depth);
        materialEditor.CommitChanges = () =>
        {
            commitMaterial?.Invoke();
            overridesEditor.ReloadBaseAsync(readMaterialId()).FireAndForget();
        };

        return (materialEditor, overridesEditor);
    }

    /// <summary>
    /// Reads a handle field's asset id from the live value cursor. Read live (not cached) so it reflects the
    /// current base after a pick.
    /// </summary>
    public static ulong ReadHandleId(IValueAccessor handleAccessor) =>
        (handleAccessor.Get() as AssetHandle?)?.Id ?? 0;

    private static PropertyDescriptor? FindField(PropertyDescriptor descriptor, string name) =>
        descriptor.Children.FirstOrDefault(child => child.Name == name);
}
