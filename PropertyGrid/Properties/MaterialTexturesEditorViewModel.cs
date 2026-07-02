using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Project.Assets;

namespace Toybox.Studio.PropertyGrid;

/// <summary>
/// The base <c>Material</c>'s <em>Textures</em> section — where a material's texture slots are defined (name +
/// default texture) with add and per-row remove. Any edit re-serialises the whole list to the lean engine shape
/// and commits it to the buffered asset (a <c>MaterialTextureBindings</c> field). Rendered as a collapsible
/// struct-style section row, matching the parameters section.
/// </summary>
public sealed partial class MaterialTexturesEditorViewModel : PropertyViewModel, IExpandable
{
    private const string Field = "textures";

    private readonly Action<JToken> _commit;

    private bool _isExpanded = true;

    public MaterialTexturesEditorViewModel(
        IReadOnlyList<MaterialTextureBinding> textures, int depth, Action<JToken> commit)
        : base(
            new PropertyDescriptor { Name = Field, Type = EngineTypes.Object, IsDefault = true },
            new JsonAccessor(null, EngineTypes.Object, null))
    {
        _commit = commit;
        Depth = depth;
        Rows = [];
        Disclosure = new DropdownPart(this);
        AddCommand = new RelayCommand(Add);

        foreach (var texture in textures)
            Rows.Add(NewRow(texture.Name, texture.Texture.Id));
    }

    public ObservableCollection<MaterialTextureRowViewModel> Rows { get; }

    /// <summary>Appends a fresh, empty texture slot to the material.</summary>
    public ICommand AddCommand { get; }

    public override bool IsComposite => true;

    public override bool HasChildren => true;

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    protected override IEnumerable<PropertyViewModel> FilterChildren => Rows.Select(row => row.Picker);

    private MaterialTextureRowViewModel NewRow(string name, ulong textureId) =>
        new(name, textureId, Depth + 1, CommitAll, RemoveRow);

    private void Add()
    {
        Rows.Add(NewRow("texture", 0));
        IsExpanded = true;
        CommitAll();
    }

    private void RemoveRow(MaterialTextureRowViewModel row)
    {
        Rows.Remove(row);
        CommitAll();
    }

    private void CommitAll()
    {
        var values = new JArray();
        foreach (var row in Rows)
            values.Add(row.ToElement());
        _commit(new JObject { ["values"] = values });
    }
}
