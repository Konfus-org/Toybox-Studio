using System;
using System.Reflection;
using Toybox.Studio.EngineApi.Types;
using Toybox.Studio.PropertyGrid;
using Toybox.Studio.PropertyGrid.Slots;
using Toybox.Studio.Utils.Attributes;
using Toybox.Studio.Utils.Composition;

namespace Toybox.Studio.Settings;

/// <summary>
/// Plugs the Project-settings asset-handle picker into a <see cref="ReflectionPropertyNodeFactory"/>: a
/// <see cref="Handle"/> property (the app Icon, the Startup World) renders as a
/// <see cref="HandlePickerValueViewModel"/> picker instead of the raw-JSON fallback. The property's
/// optional <see cref="AssetTypeAttribute"/> constrains the picker's choices.
/// </summary>
public sealed class HandlePickerValueEditor(ViewModelFactory viewModels) : IValueEditor
{
    public bool CanEdit(Type editType) => editType == typeof(Handle);

    public ValueViewModel CreateEditor(PropertyValueAccessor accessor, Type editType, PropertyInfo? property)
    {
        // The optional [AssetType] filter is the runtime argument — omitted (not passed as null) when the
        // property carries no constraint, since a null can't be matched to a parameter positionally.
        var filter = property?.GetCustomAttribute<AssetTypeAttribute>()?.Extensions;
        return filter is null
            ? viewModels.Create<HandlePickerValueViewModel>(accessor)
            : viewModels.Create<HandlePickerValueViewModel>(accessor, filter);
    }
}
