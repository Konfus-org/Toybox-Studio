using System.Reflection;
using Toybox.Studio.EngineApi.Types.Assets;
using Toybox.Studio.EngineApi.Types;
using Toybox.Studio.PropertyGrid.Slots;
using Toybox.Studio.PropertyGrid;
using Toybox.Studio.Utils.Attributes;
using Toybox.Studio.Utils.Composition;

namespace Toybox.Studio.AssetViewer;

/// <summary>
/// Plugs the asset-handle picker into a <see cref="ReflectionPropertyNodeFactory"/>: any
/// <see cref="Handle"/> property renders as a <see cref="HandleValueViewModel"/> (name link + pick +
/// open) instead of read-only text — which is what makes a material's base, its texture bindings, and
/// a renderer's model editable and openable in a plain reflection grid. The property's
/// <see cref="AssetTypeAttribute"/> constrains the picker's choices. The hosting view supplies the
/// HandleValueView template.
/// </summary>
public sealed class HandleValueEditor(ViewModelFactory viewModels) : IValueEditor
{
    public bool CanEdit(Type editType) => editType == typeof(Handle);

    public ValueViewModel CreateEditor(PropertyValueAccessor accessor, Type editType, PropertyInfo? property)
    {
        // The factory resolves the catalog / opener / popups the picker needs; the optional [AssetType]
        // filter is the runtime argument — omitted (not passed as null) when the property carries no
        // constraint, since a null can't be matched to a parameter positionally.
        var filter = property?.GetCustomAttribute<AssetTypeAttribute>()?.Extensions;
        return filter is null
            ? viewModels.Create<HandleValueViewModel>(accessor)
            : viewModels.Create<HandleValueViewModel>(accessor, filter);
    }
}
