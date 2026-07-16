using Toybox.Studio.EngineApi.Types.Assets;
using Toybox.Studio.PropertyGrid.Slots;
using Toybox.Studio.PropertyGrid;

namespace Toybox.Studio.Keybindings;

/// <summary>Edits a <see cref="KeyChordInputControl"/> value as a capture box (click, press the new
/// chord); null is "unbound". The value slot for keybinding rows and any grid over an input map.</summary>
public sealed class ChordValueViewModel(PropertyValueAccessor accessor) : ValueViewModel(accessor)
{
    public KeyChordInputControl? Value
    {
        get => Accessor.Get() as KeyChordInputControl;
        set => Accessor.Set(value);
    }
}
