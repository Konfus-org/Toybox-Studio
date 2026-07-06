using System.Text.RegularExpressions;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Projects;

/// <summary>
/// Creates new projects by duplicating the bundled default-project template (a real on-disk project
/// shipped under Templates/ beside the app), stamping the new project's name over the template's
/// <c>DefaultTemplate</c> token everywhere it appears — the CMake project/target, the settings name,
/// and the app's C++ class — so the built module matches what the launch flow loads by name
/// (<see cref="Project.Module"/>, the project folder's name).
/// </summary>
public sealed class ProjectFactory
{
    // The template's own name everywhere it appears (the CMake target, settings name, app class).
    private const string TemplateToken = "DefaultTemplate";

    // Files the token is stamped in; everything else copies byte-for-byte.
    private static readonly string[] StampedExtensions = [".txt", ".json", ".h", ".cpp"];

    private static readonly string TemplateRoot =
        Path.Combine(AppContext.BaseDirectory, "Templates", "Projects", "Default");

    /// <summary>
    /// Copies the template into <paramref name="root"/> — an empty (or not-yet-existing) folder whose
    /// name becomes the project's. The name must be identifier-shaped, because it becomes the CMake
    /// target and the app class prefix. Returns the created project root.
    /// </summary>
    public Result<string> Create(string root)
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var name = Path.GetFileName(root);
        if (!Regex.IsMatch(name, "^[A-Za-z_][A-Za-z0-9_]*$"))
            return Result<string>.Fail(
                $"'{name}' can't name a project — use letters, digits and underscores "
                + "(the name becomes the project's CMake target and app class).");

        if (!Directory.Exists(TemplateRoot))
            return Result<string>.Fail($"The bundled project template is missing ('{TemplateRoot}').");

        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
            return Result<string>.Fail($"'{root}' isn't empty — a new project needs a fresh folder.");

        try
        {
            foreach (var file in Directory.EnumerateFiles(TemplateRoot, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(root, Path.GetRelativePath(TemplateRoot, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (StampedExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                    File.WriteAllText(target, File.ReadAllText(file).Replace(TemplateToken, name));
                else
                    File.Copy(file, target);
            }
        }
        catch (Exception exception)
        {
            return Result<string>.Fail($"Creating '{name}' failed: {exception.Message}");
        }

        return Result<string>.Ok(root);
    }
}
