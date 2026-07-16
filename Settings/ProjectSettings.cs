namespace Toybox.Studio.Settings;

/// <summary>
/// The project-scoped half of the editor settings — the sections that belong to a specific project rather
/// than the user or the machine, persisted to <c>&lt;project&gt;/.toybox/ProjectSettings.json</c> (the rest
/// stay user-global in <c>~/.toybox/EditorSettings.json</c>). This is purely the on-disk shape:
/// <see cref="SettingsManager"/> reads it into (and writes it back from) the live
/// <see cref="EditorSettings"/> sections, which stay the surface the Settings grid and every consumer
/// edit, so the split is invisible above the persistence layer.
/// </summary>
public sealed class ProjectSettings
{
    public BuildEditorSettings Build { get; set; } = new();

    public GizmoEditorSettings Gizmos { get; set; } = new();

    public EditorAssetSettings EditorAssetSettings { get; set; } = new();
}
