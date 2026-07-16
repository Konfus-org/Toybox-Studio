using System.Reflection;
using Toybox.Studio.EngineApi.Types.Assets;
using Toybox.Studio.PropertyGrid.Slots;
using Toybox.Studio.PropertyGrid;
using Toybox.Studio.Utils.Composition;

namespace Toybox.Studio.Keybindings;

/// <summary>
/// Plugs the chord capture editor into a <see cref="ReflectionPropertyNodeFactory"/>: any
/// <see cref="KeyChordInputControl"/> property renders as a <see cref="ChordValueViewModel"/> instead
/// of read-only text — which is what makes an <c>.inputmap</c> asset's chords editable in a plain
/// reflection grid. The hosting view supplies the ChordValueView template.
/// </summary>
public sealed class ChordValueEditor(ViewModelFactory viewModels) : IValueEditor
{
    public bool CanEdit(Type editType) => editType == typeof(KeyChordInputControl);

    public ValueViewModel CreateEditor(PropertyValueAccessor accessor, Type editType, PropertyInfo? property) =>
        viewModels.Create<ChordValueViewModel>(accessor);
}
