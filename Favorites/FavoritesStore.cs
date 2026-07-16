using Newtonsoft.Json;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Favorites;

/// <summary>
/// The on-disk favorites: one file per "host" (the menu bar, or a named context menu) under
/// <c>~/.toybox/Favorites</c>, each a plain JSON array of the starred item ids in star order. Which host
/// means what is the <see cref="FavoritesManager"/>'s business; the store only reads and writes by name —
/// the same per-name-file shape as the dock <c>LayoutStore</c>. A missing file is the normal first-run
/// case (empty favorites); an unreadable one is kept aside as a <c>*.corrupt</c> breadcrumb and also reads
/// empty, so a bad file never takes a surface down. Only the file name is spelled through
/// <see cref="PathsCatalog"/>.
/// </summary>
public sealed class FavoritesStore
{
    private readonly PathsCatalog _paths;

    public FavoritesStore(PathsCatalog paths) => _paths = paths;

    /// <summary>The starred item ids for a host, in stored order; empty when the host has no file (or a
    /// bad one, moved aside first).</summary>
    public List<string> Load(string host)
    {
        var path = PathFor(host);
        try
        {
            if (File.Exists(path)
                && JsonConvert.DeserializeObject<List<string>>(File.ReadAllText(path)) is { } ids)
                return ids;
        }
        catch (Exception)
        {
            PreserveCorruptFile(path);
        }

        return [];
    }

    /// <summary>Writes a host's starred ids (creating the folder first). Persistence is best-effort —
    /// failing to save a favorite must never crash the editor.</summary>
    public async Task SaveAsync(string host, IReadOnlyList<string> ids)
    {
        try
        {
            var json = JsonConvert.SerializeObject(ids, Formatting.Indented);
            Directory.CreateDirectory(_paths.FavoritesDirectory);
            await File.WriteAllTextAsync(PathFor(host), json).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Best-effort; the next save overwrites it anyway.
        }
    }

    // A host maps to one file, its invalid path characters stripped; an empty result falls back so the
    // path is always valid.
    private string PathFor(string host)
    {
        var safe = string.Concat(host.Where(ch => !Path.GetInvalidFileNameChars().Contains(ch)));
        if (string.IsNullOrWhiteSpace(safe))
            safe = "favorites";
        return Path.Combine(_paths.FavoritesDirectory, safe + ".json");
    }

    private static void PreserveCorruptFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Move(path, path + ".corrupt", overwrite: true);
        }
        catch (Exception)
        {
            // Best-effort; the next save overwrites it anyway.
        }
    }
}
