using System.Collections.ObjectModel;
using System.Globalization;
using Newtonsoft.Json.Linq;
using Toybox.Studio.Services;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// Vector/quaternion property — a row of labeled scalar components.
/// </summary>
public sealed class VectorPropertyViewModel : PropertyViewModelBase
{
    private static readonly string[] Labels = ["X", "Y", "Z", "W"];

    public VectorPropertyViewModel(PropertyNode node) : base(node)
    {
        Components = [];
        if (node.Value is JArray array)
        {
            for (var index = 0; index < array.Count; index++)
            {
                var label = index < Labels.Length
                    ? Labels[index]
                    : index.ToString(CultureInfo.InvariantCulture);
                Components.Add(new VectorComponentViewModel(label, array, index, RaiseCommit));
            }
        }
    }

    public ObservableCollection<VectorComponentViewModel> Components { get; }
}
