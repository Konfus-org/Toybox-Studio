using Toybox.Studio.EngineApi.Types.Assets;
using Toybox.Studio.Utils.Attributes;

namespace Toybox.Studio.Keybindings;

/// <summary>
/// One editor action's binding, shaped for the settings grid: the action id it invokes (which also
/// labels the row) beside the chord that triggers it — null is "unbound". The name is the registered
/// action's identity, so it is construction-only and renders read-only; the chord is the editable
/// part, through the chord capture editor like any <see cref="KeyChordInputControl"/> value.
///
/// A freshly constructed <c>Keybinding(name, defaultChord)</c> is the action's default: <see cref="Chord"/>
/// starts at the registered <paramref name="defaultChord"/>, so the grid's default twin — which it builds
/// by reconstructing an instance from its constructor — compares and resets against that chord. Loading
/// the current binding overrides <see cref="Chord"/> after construction; the hidden <see cref="DefaultChord"/>
/// rides along as the reset reference.
/// </summary>
public sealed class Keybinding(string name, KeyChordInputControl? defaultChord = null)
{
    /// <summary>The action id the chord invokes (an <c>InputAction</c> name in the keymap).</summary>
    public string Name { get; } = name;

    /// <summary>The chord that triggers the action — starts at the registered default, null when unbound.</summary>
    public KeyChordInputControl? Chord { get; set; } = defaultChord;

    /// <summary>The action's registered default chord — what the state dot compares against and
    /// reset restores; null when the action ships unbound.</summary>
    [Hidden]
    public KeyChordInputControl? DefaultChord { get; } = defaultChord;
}
