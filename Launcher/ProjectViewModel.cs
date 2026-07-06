using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Toybox.Studio.Projects;

namespace Toybox.Studio;

/// <summary>
/// A project as the picker lists it: its icon (the Toybox logo when the project doesn't set one), the
/// name to lead with, the full root path, and the row tooltip — the double-click-to-open hint plus a
/// preview of the project's README when it has one.
/// </summary>
public sealed class ProjectViewModel(Project project)
{
    private static readonly Uri FallbackIcon = new("avares://Toybox.Studio.Resources/Icons/Toybox.png");

    // Enough README for a taste without turning the tooltip into a wall.
    private const int ReadmePreviewLimit = 700;

    public string Name { get; } = project.Name;

    public string Path { get; } = project.Path;

    public Bitmap Icon { get; } = project.Icon ?? new Bitmap(AssetLoader.Open(FallbackIcon));

    public string Tooltip { get; } = BuildTooltip(project.Path);

    private static string BuildTooltip(string root)
    {
        const string hint = "Double-click to open.";
        var readme = ProjectLoader.PeekReadme(root);
        if (readme is null)
            return hint;

        if (readme.Length > ReadmePreviewLimit)
            readme = readme[..ReadmePreviewLimit].TrimEnd() + "…";
        return $"{hint}\n\n{readme}";
    }
}
