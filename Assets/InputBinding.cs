namespace Toybox.Studio.Assets;

/// <summary>One physical control driving an action, with a per-binding scale
/// (sensitivity/inversion), mirroring the engine's <c>InputBinding</c>.</summary>
public sealed record InputBinding
{
    public InputControl Control { get; init; } = new KeyboardInputControl();

    public float Scale { get; init; } = 1f;
}
