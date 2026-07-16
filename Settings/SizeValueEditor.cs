using System;
using System.Reflection;
using Toybox.Studio.EngineApi.Types;
using Toybox.Studio.PropertyGrid;
using Toybox.Studio.PropertyGrid.Slots;
using Toybox.Studio.Utils.Composition;

namespace Toybox.Studio.Settings;

/// <summary>
/// Plugs the width/height <see cref="Size"/> editor into a <see cref="ReflectionPropertyNodeFactory"/>: a
/// <see cref="Size"/> property (the graphics <c>Resolution</c>) renders as two numeric fields via
/// <see cref="SizeValueViewModel"/> instead of the raw-JSON fallback the grid otherwise gives a
/// value-type record it can't recurse into.
/// </summary>
public sealed class SizeValueEditor(ViewModelFactory viewModels) : IValueEditor
{
    public bool CanEdit(Type editType) => editType == typeof(Size);

    public ValueViewModel CreateEditor(PropertyValueAccessor accessor, Type editType, PropertyInfo? property) =>
        viewModels.Create<SizeValueViewModel>(accessor);
}
