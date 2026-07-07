namespace Toybox.Studio.Settings;

/// <summary>How presentation syncs with the display refresh rate, mirroring the engine's
/// <c>VsyncMode</c>.</summary>
public enum VsyncMode
{
    Off = 0,
    On,
    Adaptive,
}
