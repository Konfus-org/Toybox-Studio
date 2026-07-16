using Avalonia.Input;
using Toybox.Studio.EngineApi.Types.Assets;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Keybindings;

/// <summary>
/// Translates between the keymap's engine-format chords (<see cref="KeyChordInputControl"/>) and
/// Avalonia's key vocabulary: pressed keys become chords (the dispatcher and the capture control),
/// chords become <see cref="KeyGesture"/>s (menu hints and display strings). The reverse key map is
/// derived from <see cref="EngineInputTranslator.Map"/> so the two directions can never disagree.
/// </summary>
public static class KeyChordGestures
{
    private static readonly Lazy<IReadOnlyDictionary<InputKey, Key>> ReverseMap = new(BuildReverseMap);

    /// <summary>The chord the pressed key + modifiers describe; null when the key has no engine
    /// mapping or is itself a modifier (a chord needs a non-modifier key).</summary>
    public static KeyChordInputControl? FromKey(Key key, KeyModifiers modifiers)
    {
        var mapped = EngineInputTranslator.Map(key);
        if (mapped == InputKey.Unknown || IsModifier(mapped))
            return null;

        return new KeyChordInputControl
        {
            Key = mapped,
            Ctrl = modifiers.HasFlag(KeyModifiers.Control),
            Shift = modifiers.HasFlag(KeyModifiers.Shift),
            Alt = modifiers.HasFlag(KeyModifiers.Alt),
            Gui = modifiers.HasFlag(KeyModifiers.Meta),
        };
    }

    /// <summary>The chord as an Avalonia gesture (for a menu item's shortcut hint); null when the
    /// chord's key has no Avalonia counterpart.</summary>
    public static KeyGesture? ToGesture(KeyChordInputControl chord)
    {
        if (!ReverseMap.Value.TryGetValue(chord.Key, out var key))
            return null;

        var modifiers = KeyModifiers.None;
        if (chord.Ctrl)
            modifiers |= KeyModifiers.Control;
        if (chord.Shift)
            modifiers |= KeyModifiers.Shift;
        if (chord.Alt)
            modifiers |= KeyModifiers.Alt;
        if (chord.Gui)
            modifiers |= KeyModifiers.Meta;
        return new KeyGesture(key, modifiers);
    }

    /// <summary>The chord as display text ("Ctrl+Shift+S"), falling back to the engine key's name for
    /// keys Avalonia can't express.</summary>
    public static string ToDisplayString(KeyChordInputControl chord)
    {
        if (ToGesture(chord) is { } gesture)
            return gesture.ToString();

        var parts = new List<string>(5);
        if (chord.Ctrl)
            parts.Add("Ctrl");
        if (chord.Shift)
            parts.Add("Shift");
        if (chord.Alt)
            parts.Add("Alt");
        if (chord.Gui)
            parts.Add("Win");
        parts.Add(chord.Key.ToString());
        return string.Join("+", parts);
    }

    /// <summary>Whether the engine key is a modifier (a chord's key must not be one).</summary>
    public static bool IsModifier(InputKey key) => key is InputKey.LCtrl or InputKey.RCtrl
        or InputKey.LShift or InputKey.RShift
        or InputKey.LAlt or InputKey.RAlt
        or InputKey.LGui or InputKey.RGui;

    private static IReadOnlyDictionary<InputKey, Key> BuildReverseMap()
    {
        // Derive the reverse mapping by probing the forward one; the first Avalonia key claiming an
        // engine key wins (aliases collapse).
        var map = new Dictionary<InputKey, Key>();
        foreach (var key in Enum.GetValues<Key>())
        {
            var mapped = EngineInputTranslator.Map(key);
            if (mapped != InputKey.Unknown && !map.ContainsKey(mapped))
                map[mapped] = key;
        }

        return map;
    }
}
