using Toybox.Studio.EngineApi.Types;
namespace Toybox.Studio.EngineApi.Types.Assets;

/// <summary>
/// The physical control an <see cref="InputBinding"/> reads, mirroring the engine's
/// <c>InputControl</c> variant — each derived record is one variant alternative, and the wire carries
/// the alternative's snake_case type name as its discriminator (see
/// <see cref="InputSchemeListConverter"/>).
/// </summary>
public abstract record InputControl;
