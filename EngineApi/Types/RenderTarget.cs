using Toybox.Studio.EngineApi;

namespace Toybox.Studio.EngineApi.Types;

/// <summary>
/// The surface a camera renders into — a native window or an in-memory texture — mirroring the
/// engine's <c>RenderTarget</c> (a <see cref="EngineApi.Handle"/> plus its size; the native window
/// pointer is engine-side only and never travels).
/// </summary>
public sealed record RenderTarget
{
    public Handle Handle { get; init; }

    public Size Size { get; init; }
}
