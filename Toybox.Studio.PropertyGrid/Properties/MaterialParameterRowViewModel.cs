using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.PropertyGrid;

/// <summary>
/// One parameter DEFINITION row on a base <c>Material</c>: its shader binding name, its value type, and its
/// default value — the three things that make up the material's parameter shape. Editing any of them raises the
/// owning section's commit so the whole parameter list is re-serialised and pushed to the buffered asset. The
/// default-value editor is the same type-driven leaf widget the rest of the grid uses (built via
/// <see cref="PropertyViewModelFactory"/>), rebuilt with a fresh default whenever the type changes.
/// </summary>
public sealed partial class MaterialParameterRowViewModel : ObservableObject
{
    // Friendly dropdown label → the variant alternative's wire token. Restricted to the common shader set (each
    // has a first-class grid editor); the list order is the dropdown order.
    private static readonly IReadOnlyList<(string Label, string Token)> TypeTable =
    [
        ("Bool", EngineTypes.Bool),
        ("Int", EngineTypes.Int),
        ("Float", EngineTypes.Float),
        ("Vec2", EngineTypes.Vec2),
        ("Vec3", EngineTypes.Vec3),
        ("Vec4", EngineTypes.Vec4),
        ("Color", EngineTypes.Color),
    ];

    private readonly Action _changed;
    private readonly int _depth;

    // The live variant wrapper this row edits: { "type":"variant", "value":{ "type":<token>, "value":<bare> } }.
    // The value editor binds to the inner bare token and mutates it in place; a type change replaces the wrapper
    // wholesale. Kept in the engine's lean shape so the section serialises it straight into the saved material.
    private JObject _data;

    // True while (re)building from a new type/value so the property-changed hooks don't re-commit mid-construction.
    private bool _building;

    // Seeded directly (bypassing the change hooks) at construction, then edited live by the view.
    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string _selectedType;

    [ObservableProperty]
    private PropertyViewModel _editor = null!;

    public MaterialParameterRowViewModel(
        string name, JToken? data, int depth, Action changed, Action<MaterialParameterRowViewModel> remove)
    {
        _depth = depth;
        _changed = changed;
        _name = name;
        _data = NormaliseVariant(data);
        _selectedType = LabelFor(ActiveToken(_data));
        RemoveCommand = new RelayCommand(() => remove(this));
        RebuildEditor();
    }

    /// <summary>The value-type choices shown in the row's dropdown.</summary>
    public IReadOnlyList<string> Types { get; } = TypeTable.Select(entry => entry.Label).ToList();

    /// <summary>Deletes this parameter from its material.</summary>
    public ICommand RemoveCommand { get; }

    public int Depth => _depth;

    /// <summary>
    /// This row as one lean <c>{ name, data }</c> element for the material's parameter list — the exact shape the
    /// engine deserialises (the <c>data</c> stays the bare variant wrapper).
    /// </summary>
    public JObject ToElement() => new()
    {
        ["name"] = new JValue(Name),
        ["data"] = _data.DeepClone(),
    };

    partial void OnNameChanged(string value)
    {
        if (!_building)
            _changed();
    }

    partial void OnSelectedTypeChanged(string value)
    {
        if (_building)
            return;

        var token = TokenFor(value);
        _data = MakeVariant(token, DefaultValue(token));
        RebuildEditor();
        _changed();
    }

    // Rebuilds the value editor from the current variant. ParseValueNode unwraps the variant and references the
    // live inner value token, so leaf edits land straight back in _data; the leaf's commit raises the section's.
    private void RebuildEditor()
    {
        _building = true;
        try
        {
            var (descriptor, accessor) = JsonDescriptorReader.ReadValue(Name, _data, _changed);
            Editor = PropertyViewModelFactory.Create(descriptor, accessor, _depth + 1);
        }
        finally
        {
            _building = false;
        }
    }

    // Coerces stored data to a variant of one of the supported types, defaulting to a float when it is missing,
    // malformed, or an unsupported alternative (matrix/double) that this editor can't present.
    private static JObject NormaliseVariant(JToken? data)
    {
        if (data is JObject variant
            && variant.Value<string>(EngineKeys.Type) == EngineTypes.Variant
            && variant[EngineKeys.Value] is JObject alternative
            && alternative.Value<string>(EngineKeys.Type) is { } token
            && TypeTable.Any(entry => entry.Token == token))
        {
            return (JObject)variant.DeepClone();
        }

        return MakeVariant(EngineTypes.Float, DefaultValue(EngineTypes.Float));
    }

    private static JObject MakeVariant(string token, JToken value) => new()
    {
        [EngineKeys.Type] = EngineTypes.Variant,
        [EngineKeys.Value] = new JObject { [EngineKeys.Type] = token, [EngineKeys.Value] = value },
    };

    private static string ActiveToken(JObject variant) =>
        (variant[EngineKeys.Value] as JObject)?.Value<string>(EngineKeys.Type) ?? EngineTypes.Float;

    // A sensible zero/identity default for each supported type, in the bare wire shape the value editors expect.
    private static JToken DefaultValue(string token) => token switch
    {
        EngineTypes.Bool => new JValue(false),
        EngineTypes.Int => new JValue(0),
        EngineTypes.Vec2 => new JArray(0f, 0f),
        EngineTypes.Vec3 => new JArray(0f, 0f, 0f),
        EngineTypes.Vec4 => new JArray(0f, 0f, 0f, 0f),
        EngineTypes.Color => new JObject { ["r"] = 1f, ["g"] = 1f, ["b"] = 1f, ["a"] = 1f },
        _ => new JValue(0f),
    };

    private static string LabelFor(string token) =>
        TypeTable.FirstOrDefault(entry => entry.Token == token).Label is { Length: > 0 } label ? label : "Float";

    private static string TokenFor(string label) =>
        TypeTable.FirstOrDefault(entry => entry.Label == label).Token is { Length: > 0 } token ? token : EngineTypes.Float;
}
