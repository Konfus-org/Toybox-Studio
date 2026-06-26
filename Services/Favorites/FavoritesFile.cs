using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Toybox.Studio.Services.Settings;

namespace Toybox.Studio.Services.Favorites;

/// <summary>
/// The on-disk favorites: per menu/toolbar "host", the ids of the items the user has starred. Stored as
/// <c>Favorites/favorites.json</c> under the user's .toybox folder, alongside the editor settings and themes.
/// Loads and saves itself; a missing or unreadable file yields empty favorites (the unreadable file is kept
/// aside as a <c>*.corrupt</c> breadcrumb) — the same tolerant pattern as <see cref="EditorSettings"/>.
/// </summary>
public sealed class FavoritesFile
{
    private static readonly string Directory =
        Path.Combine(EditorSettings.BaseDirectory, "Favorites");

    private static readonly string FilePath = Path.Combine(Directory, "favorites.json");

    /// <summary>Host key (e.g. <c>worldTree.entity</c>, <c>toolbar</c>) → the starred item ids in that host.</summary>
    public Dictionary<string, List<string>> Hosts { get; set; } = new(StringComparer.Ordinal);

    public static FavoritesFile Load()
    {
        try
        {
            if (File.Exists(FilePath)
                && JsonConvert.DeserializeObject<FavoritesFile>(File.ReadAllText(FilePath)) is { } loaded)
                return loaded;
        }
        catch (Exception)
        {
            PreserveCorruptFile();
        }

        return new FavoritesFile();
    }

    public async Task SaveAsync()
    {
        System.IO.Directory.CreateDirectory(Directory);
        var json = JsonConvert.SerializeObject(this, Formatting.Indented);
        await File.WriteAllTextAsync(FilePath, json).ConfigureAwait(false);
    }

    private static void PreserveCorruptFile()
    {
        try
        {
            if (File.Exists(FilePath))
                File.Move(FilePath, FilePath + ".corrupt", overwrite: true);
        }
        catch (Exception)
        {
            // Best-effort; the next save overwrites it anyway.
        }
    }
}
