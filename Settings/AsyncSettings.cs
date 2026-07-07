namespace Toybox.Studio.Settings;

/// <summary>
/// The app's asynchronous runtime configuration, mirroring the engine's <c>AsyncSettings</c>. A record
/// value: edit by assigning a changed copy back to the owning <see cref="AppSettings"/>, which is what
/// pushes it.
/// </summary>
public sealed record AsyncSettings
{
    /// <summary>The async worker thread count; zero lets the engine pick.</summary>
    public int WorkerCount { get; init; }
}
