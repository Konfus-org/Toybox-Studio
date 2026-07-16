using Toybox.Studio.EngineApi.Types.Assets;
using Icon = IconPacks.Avalonia.Lucide.PackIconLucideKind;

namespace Toybox.Studio.Keybindings;

/// <summary>
/// One invokable editor action's metadata: its stable <see cref="Id"/> (see <see cref="ActionIds"/>),
/// how it presents (title/category/icon), the keymap scheme (scope) its bindings live in, and the
/// chords it ships bound to. Pure data — execution belongs to whoever handles the
/// <see cref="EditorActionInvoked"/> event carrying the id, so registrars and executors stay
/// decoupled through the event bus.
/// </summary>
public sealed record EditorAction
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    /// <summary>Groups the action in the keybindings page ("Build", "Window", …).</summary>
    public string Category { get; init; } = "";

    public Icon Icon { get; init; }

    /// <summary>The keymap scheme (scope) the action's bindings live in; global by default.</summary>
    public string Scheme { get; init; } = Keybindings.Scheme.Global;

    /// <summary>The chords the action is bound to out of the box; rebindable, of course.</summary>
    public IReadOnlyList<KeyChordInputControl> DefaultChords { get; init; } = [];
}
