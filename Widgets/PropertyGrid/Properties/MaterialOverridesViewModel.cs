using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json.Linq;
using Toybox.Studio.Services.EngineApi;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// The override editor for a <c>MaterialInstance</c>: it shows the referenced material asset's
/// <em>base</em> parameters and textures as fixed, editable slots (fetched via <c>asset.describe</c>),
/// merged with the instance's per-slot overrides. Editing a slot writes an override; resetting it reverts
/// to the base value. The shape is locked to the material — slots come only from the base asset, so there is
/// no add/remove. Config is intentionally absent (it lives on the asset, shared by every instance).
/// </summary>
public sealed partial class MaterialOverridesViewModel : PropertyViewModel
{
    private readonly JArray _paramOverrides;
    private readonly JArray _textureOverrides;
    private readonly EngineRpc _engine;
    private readonly Action? _commit;

    public MaterialOverridesViewModel(PropertyNode node, long materialId, EngineRpc engine, Action? commit)
        : base(node)
    {
        _engine = engine;
        _commit = commit;

        // The overrides body is { textures: { …, value: [ … ] }, parameters: { …, value: [ … ] } }; the live
        // arrays are where per-slot overrides are written.
        var body = node.Value as JObject;
        _paramOverrides = ArrayField(body, "parameters");
        _textureOverrides = ArrayField(body, "textures");

        Parameters = [];
        Textures = [];
        ReloadBaseAsync(materialId).FireAndForget();
    }

    public override bool IsComposite => true;

    public override bool HasChildren => true;

    public ObservableCollection<MaterialSlotViewModel> Parameters { get; }

    public ObservableCollection<MaterialSlotViewModel> Textures { get; }

    public bool HasParameters => Parameters.Count > 0;

    public bool HasTextures => Textures.Count > 0;

    /// <summary>True once a base material has been resolved but it exposes no parameters or textures.</summary>
    [ObservableProperty]
    public partial bool IsEmpty { get; private set; }

    protected override IEnumerable<PropertyViewModel> FilterChildren =>
        Textures.Select(slot => slot.Editor).Concat(Parameters.Select(slot => slot.Editor));

    /// <summary>
    /// Re-fetches the base material by id and rebuilds the slot rows. Called on construction and whenever the
    /// Base material is changed. A zero id (no material assigned) just clears the slots.
    /// </summary>
    public async Task ReloadBaseAsync(long materialId)
    {
        Parameters.Clear();
        Textures.Clear();
        NotifyCounts();

        if (materialId == 0)
            return;

        var result = await _engine.DescribeAssetAsync(materialId, CancellationToken.None)
            .ContinueOnSameContext();
        if (!result.Success || result.Value?["material"] is not JObject material)
            return;

        AddSlots(material["textures"], _textureOverrides, "texture", Textures);
        AddSlots(material["parameters"], _paramOverrides, "data", Parameters);
        NotifyCounts();
    }

    private void AddSlots(
        JToken? field,
        JArray overrides,
        string valueKey,
        ObservableCollection<MaterialSlotViewModel> into)
    {
        if (Inner(field) is not JArray elements)
            return;

        foreach (var element in elements.OfType<JObject>())
            if (ReadName(element) is { Length: > 0 } name)
                into.Add(new MaterialSlotViewModel(name, element, overrides, valueKey, _commit));
    }

    private void NotifyCounts()
    {
        OnPropertyChanged(nameof(HasParameters));
        OnPropertyChanged(nameof(HasTextures));
        IsEmpty = Parameters.Count == 0 && Textures.Count == 0;
    }

    // The live array inside a typed field wrapper ({ attributes/type, value: [...] }), or the token itself
    // when it is already bare.
    private static JArray ArrayField(JObject? body, string name) =>
        Inner(body?[name]) as JArray ?? [];

    private static JToken? Inner(JToken? token) =>
        token is JObject obj && obj.TryGetValue("value", out var value) ? value : token;

    private static string? ReadName(JObject element) =>
        Inner(element["name"])?.Value<string>();
}
