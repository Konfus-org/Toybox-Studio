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
/// The base <c>Material</c>'s <em>Parameters</em> section — the one place a material's parameter shape is
/// defined. It shows one editable <see cref="MaterialParameterRowViewModel"/> per parameter (name / value type /
/// default value) with add and per-row remove, so authoring a material means declaring the parameters its
/// instances then override. Any edit re-serialises the whole list to the lean engine shape and commits it to the
/// buffered asset (a <c>MaterialParameterBindings</c> field). Rendered as a collapsible struct-style section row.
/// </summary>
public sealed partial class MaterialParametersEditorViewModel : PropertyViewModel, IExpandable
{
    // The material's "parameters" field name — kept as RawName so the asset inspector's render-category filter
    // (MaterialFields) still finds and shows this section like the generic row it replaces.
    private const string Field = "parameters";

    private readonly Action<JToken> _commit;

    private bool _isExpanded = true;

    public MaterialParametersEditorViewModel(
        IReadOnlyList<MaterialParameter> parameters, int depth, Action<JToken> commit)
        : base(
            new PropertyDescriptor { Name = Field, Type = EngineTypes.Object, IsDefault = true },
            new JsonAccessor(null, EngineTypes.Object, null))
    {
        _commit = commit;
        Depth = depth;
        Rows = [];
        Disclosure = new DropdownPart(this);
        AddCommand = new RelayCommand(Add);

        foreach (var parameter in parameters)
            Rows.Add(NewRow(parameter.Name, parameter.Data));
    }

    public ObservableCollection<MaterialParameterRowViewModel> Rows { get; }

    /// <summary>Appends a fresh parameter (a Float defaulting to 0) to the material.</summary>
    public ICommand AddCommand { get; }

    public override bool IsComposite => true;

    public override bool HasChildren => true;

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    protected override IEnumerable<PropertyViewModel> FilterChildren => Rows.Select(row => row.Editor);

    private MaterialParameterRowViewModel NewRow(string name, JToken? data) =>
        new(name, data, Depth + 1, CommitAll, RemoveRow);

    private void Add()
    {
        Rows.Add(NewRow("parameter", null));
        IsExpanded = true;
        CommitAll();
    }

    private void RemoveRow(MaterialParameterRowViewModel row)
    {
        Rows.Remove(row);
        CommitAll();
    }

    // Serialises every row to a lean { values: [ { name, data }, … ] } token and commits it as the material's
    // whole "parameters" field. Committing a hand-built lean token keeps the saved material in exact engine shape.
    private void CommitAll()
    {
        var values = new JArray();
        foreach (var row in Rows)
            values.Add(row.ToElement());
        _commit(new JObject { ["values"] = values });
    }
}
