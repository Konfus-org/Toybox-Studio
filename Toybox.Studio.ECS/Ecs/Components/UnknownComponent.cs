namespace Toybox.Studio.Ecs.Components;

/// <summary>
/// A component whose engine wire name has no typed <see cref="Component"/> subclass — the fallback the parser
/// mints so every component is still a real <see cref="Component"/> (identity + behavioural handle +
/// <see cref="EngineApi.EngineSyncedObject.Raw"/> the inspector grid renders), it just models no typed
/// fields of its own. Mirrors how <c>UnknownAsset</c> backs an unrecognised asset type.
/// </summary>
public sealed class UnknownComponent : Component
{
}
