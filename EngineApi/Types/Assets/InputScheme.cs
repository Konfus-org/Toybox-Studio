using Toybox.Studio.EngineApi.Types;
namespace Toybox.Studio.EngineApi.Types.Assets;

/// <summary>
/// A named, activatable group of actions, mirroring the engine's <c>InputScheme</c>. For editor
/// keybindings a scheme is a scope — "Global", "Viewport" — activated by where focus sits. A record
/// value: edit by assigning a changed copy back to the owning <see cref="InputMap"/>.
/// </summary>
public sealed record InputScheme
{
    public string Name { get; init; } = string.Empty;

    /// <summary>Whether the scheme starts active when the map loads.</summary>
    public bool IsActive { get; init; }

    public IReadOnlyList<InputAction> Actions { get; init; } = [];

    public InputAction? GetAction(string name) =>
        Actions.FirstOrDefault(action => action.Name == name);

    /// <summary>This scheme with <paramref name="action"/> replaced in place (or appended).</summary>
    public InputScheme SetAction(InputAction action)
    {
        var actions = new List<InputAction>(Actions.Count + 1);
        var replaced = false;
        foreach (var existing in Actions)
        {
            actions.Add(existing.Name == action.Name ? action : existing);
            replaced |= existing.Name == action.Name;
        }

        if (!replaced)
            actions.Add(action);
        return this with { Actions = actions };
    }

    /// <summary>This scheme without the named action.</summary>
    public InputScheme RemoveAction(string name) =>
        this with { Actions = [.. Actions.Where(action => action.Name != name)] };
}
