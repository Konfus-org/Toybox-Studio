namespace Toybox.Studio.Assets;

/// <summary>
/// One named action and the bindings that drive it, mirroring the data half of the engine's
/// <c>InputAction</c> (callbacks and runtime value state are engine-side only). The name doubles as
/// the editor's action id — what a keybinding invokes. A record value: edit by assigning a changed
/// copy back into the owning <see cref="InputScheme"/>.
/// </summary>
public sealed record InputAction
{
    public string Name { get; init; } = string.Empty;

    public InputActionValueType ValueType { get; init; } = InputActionValueType.Button;

    public IReadOnlyList<InputBinding> Bindings { get; init; } = [];
}
