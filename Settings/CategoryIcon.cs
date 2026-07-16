namespace Toybox.Studio.Settings;

/// <summary>
/// The icon a browser category tags its assets with. A curated, domain-named set the settings grid offers
/// as a dropdown — the browser maps each to a concrete Lucide glyph. Keeping it an editor-defined enum
/// (rather than the icon pack's 1600-member kind) keeps the settings data free of UI-package references
/// and the picker short and meaningful.
/// </summary>
public enum CategoryIcon
{
    File,
    Folder,
    Model,
    Blender,
    Texture,
    Material,
    Shader,
    Audio,
    Script,
    Document,
    Config,
    Input,
    World,
    Settings,
}
