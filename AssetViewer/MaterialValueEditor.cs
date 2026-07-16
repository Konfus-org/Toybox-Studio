using System.Reflection;
using Toybox.Studio.EngineApi.Types.Assets;
using Toybox.Studio.PropertyGrid.Slots;
using Toybox.Studio.PropertyGrid;
using Toybox.Studio.Utils.Composition;

namespace Toybox.Studio.AssetViewer;

/// <summary>
/// Plugs the shader-parameter value editor into a <see cref="ReflectionPropertyNodeFactory"/>: any
/// <see cref="MaterialValue"/> property renders as a <see cref="MaterialValueViewModel"/> (kind selector
/// + typed editor) instead of read-only text — which is what makes a material's parameters editable in
/// the reflection grid. The hosting view supplies the MaterialValueView template.
/// </summary>
public sealed class MaterialValueEditor(ViewModelFactory viewModels) : IValueEditor
{
    public bool CanEdit(Type editType) => editType == typeof(MaterialValue);

    public ValueViewModel CreateEditor(PropertyValueAccessor accessor, Type editType, PropertyInfo? property) =>
        viewModels.Create<MaterialValueViewModel>(accessor);
}
