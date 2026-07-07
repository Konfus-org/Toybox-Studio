using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Settings;

/// <summary>
/// The app's world streaming and chunking configuration, mirroring the engine's <c>WorldSettings</c>.
/// A record value: edit by assigning a changed copy back to the owning <see cref="AppSettings"/>,
/// which is what pushes it.
/// </summary>
public sealed record WorldSettings
{
    /// <summary>The world the app opens into (a <c>.world</c> asset).</summary>
    public Handle StartupWorld { get; init; }

    /// <summary>The world-space size of a streamed chunk's cube.</summary>
    public float ChunkSize { get; init; } = 32f;

    /// <summary>The world-unit radius around each camera within which streamed chunks stay loaded
    /// regardless of view; zero is view-only.</summary>
    public float KeepLoadedRadius { get; init; } = 64f;
}
