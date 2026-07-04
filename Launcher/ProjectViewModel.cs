using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Toybox.Studio.Projects;

namespace Toybox.Studio;

/// <summary>
/// A recent project as the picker lists it: its icon (the Toybox logo when the project doesn't set
/// one), the name to lead with, and the full root path.
/// </summary>
public sealed class ProjectViewModel(Project project)
{
    private static readonly Uri FallbackIcon = new("avares://Toybox.Studio.Resources/Icons/Toybox.png");

    public string Name { get; } = project.Name;

    public string Path { get; } = project.Path;

    public Bitmap Icon { get; } = project.Icon ?? new Bitmap(AssetLoader.Open(FallbackIcon));
}
