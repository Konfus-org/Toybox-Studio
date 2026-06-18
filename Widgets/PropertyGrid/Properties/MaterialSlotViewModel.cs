using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json.Linq;
using Toybox.Studio.Services.EngineApi;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// One base-material slot — a parameter or texture <em>defined by the material asset</em> — shown as an
/// editable row. The row starts at the base value; editing it promotes the slot to a per-instance override
/// (the entry is created in the live overrides array on first edit), and the row's reset reverts to the base
/// value by dropping the override. The slot set is fixed by the material, so there is no add/remove here —
/// you can change a value but not the material's shape.
/// </summary>
public sealed partial class MaterialSlotViewModel : ObservableObject
{
    private readonly string _name;

    // One element from the base material's parameters/textures array ({ name, data } or { name, texture }),
    // used as the template when promoting this slot to an override.
    private readonly JObject _baseElement;

    // The live overrides array (overrides.parameters or overrides.textures) this slot writes into.
    private readonly JArray _overrides;

    // The element's value field — "data" for a parameter, "texture" for a texture.
    private readonly string _valueKey;

    // Raises the owning component's whole-overrides commit (reflect.set on "overrides").
    private readonly Action? _commit;

    // The grid nesting level this slot's editor renders at, so it indents under its material/category parent.
    private readonly int _depth;

    // The value wrapper the current editor is bound to: the live override token when overridden, else a
    // detached clone of the base value (adopted into the array on first edit). Kept so the promote step can
    // splice the in-progress edit into the array instead of the base value.
    private JToken _wrapper = JValue.CreateNull();

    public MaterialSlotViewModel(
        string name, JObject baseElement, JArray overrides, string valueKey, Action? commit, int depth)
    {
        _name = name;
        _baseElement = baseElement;
        _overrides = overrides;
        _valueKey = valueKey;
        _commit = commit;
        _depth = depth;
        RebuildEditor();
    }

    /// <summary>The leaf widget editing this slot's value, rebuilt on reset so it re-reads the base value.</summary>
    [ObservableProperty]
    public partial PropertyViewModel Editor { get; private set; } = null!;

    private JObject? FindOverride()
    {
        foreach (var token in _overrides)
            if (token is JObject entry && ReadName(entry) == _name)
                return entry;
        return null;
    }

    private static string? ReadName(JObject entry) =>
        (entry["name"] is JObject wrapper && wrapper.TryGetValue("value", out var inner) ? inner : entry["name"])
        ?.Value<string>();

    private void RebuildEditor()
    {
        var existing = FindOverride();
        _wrapper = existing?[_valueKey] ?? _baseElement[_valueKey]!.DeepClone();

        var node = JsonParser.ParseValueNode(_name, _wrapper);
        var editor = PropertyViewModelFactory.Create(node, OnEdited, _depth);
        editor.ResetToDefault = OnReset;
        editor.IsModified = existing is not null;
        Editor = editor;
    }

    // First edit promotes the slot to an override: clone the base entry (so the name + wire shape match what
    // the engine deserializes) but adopt the live edited value wrapper so this and later edits land directly
    // in the array. Once the entry exists, editing just re-commits.
    private void OnEdited()
    {
        if (FindOverride() is null)
        {
            var entry = (JObject)_baseElement.DeepClone();
            entry[_valueKey] = _wrapper;
            _overrides.Add(entry);
        }

        _commit?.Invoke();
        Editor.IsModified = true;
    }

    private void OnReset()
    {
        FindOverride()?.Remove();
        RebuildEditor();
        _commit?.Invoke();
    }
}
