namespace Toybox.Studio.Settings;

/// <summary>The graphics backend to activate, mirroring the engine's <c>GraphicsApi</c>. The member
/// spellings follow the engine's wire names ("opengl", "directx"), not C# casing.</summary>
public enum GraphicsApi
{
    None = 0,
    Vulkan,
    Opengl,
    Directx,
    Metal,
    Custom,
}
