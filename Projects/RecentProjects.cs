using Toybox.Studio.Settings;

namespace Toybox.Studio.Projects;

/// <summary>
/// Maintains the editor settings' recent-projects list — the last-opened path plus a capped
/// most-recently-used list — shared by the launch flow and the in-process project switch so the rules
/// live in one place. Mutates the settings in place; the caller persists (<c>SettingsManager.ApplyAsync</c>).
/// </summary>
public static class RecentProjects
{
    private const int MaxRecent = 10;

    /// <summary>Records <paramref name="root"/> as the last-opened project and moves it to the front of
    /// the recents (de-duplicated, capped).</summary>
    public static void Remember(ProjectEditorSettings projects, string root)
    {
        projects.LastOpened = root;
        projects.Recent.RemoveAll(path => string.Equals(path, root, StringComparison.OrdinalIgnoreCase));
        projects.Recent.Insert(0, root);
        if (projects.Recent.Count > MaxRecent)
            projects.Recent.RemoveRange(MaxRecent, projects.Recent.Count - MaxRecent);
    }
}
