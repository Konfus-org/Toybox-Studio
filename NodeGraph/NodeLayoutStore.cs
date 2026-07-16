using Newtonsoft.Json;
using Toybox.Studio.Utils;

namespace Toybox.Studio.NodeGraph;

/// <summary>
/// The on-disk node layouts: one file per world under the open project's <c>.toybox/nodes</c> folder,
/// each a JSON map of entity id → <see cref="NodeLayout"/>. Project-local (it belongs to the project's
/// content, not the user profile) — the same per-key-file shape as the dock <c>LayoutStore</c> and
/// <c>FavoritesStore</c>, but rooted at the open project's <c>.toybox</c> (via <see cref="ProjectPaths"/>).
/// A missing file is the normal first-run
/// case (all nodes at their auto default); an unreadable one is kept aside as a <c>*.corrupt</c> breadcrumb
/// and reads empty, so a bad file never takes the overlay down. The store only reads and writes by world
/// id; which entities matter is the <see cref="NodeLayoutManager"/>'s business.
/// </summary>
public sealed class NodeLayoutStore
{
    private readonly ProjectPaths _paths;

    // Deserialize the map by REPLACING, not populating, so a reload can't stack onto a default-initialized
    // dictionary (the same Newtonsoft gotcha SettingsManager guards against).
    private static readonly JsonSerializerSettings ReplaceCollections =
        new() { ObjectCreationHandling = ObjectCreationHandling.Replace };

    public NodeLayoutStore(ProjectPaths paths) => _paths = paths;

    /// <summary>The saved node layouts for a world, keyed by entity id; empty when the world has no file
    /// (or a bad one, moved aside first).</summary>
    public Dictionary<ulong, NodeLayout> Load(ulong worldId)
    {
        var path = PathFor(worldId);
        try
        {
            if (File.Exists(path)
                && JsonConvert.DeserializeObject<Dictionary<ulong, NodeLayout>>(
                    File.ReadAllText(path), ReplaceCollections) is { } map)
                return map;
        }
        catch (Exception)
        {
            PreserveCorruptFile(path);
        }

        return new Dictionary<ulong, NodeLayout>();
    }

    /// <summary>Writes a world's node layouts (already serialized by the caller for a race-free snapshot),
    /// creating the folder first. Persistence is best-effort — failing to save a node position must never
    /// crash the editor.</summary>
    public async Task SaveAsync(ulong worldId, string json)
    {
        try
        {
            Directory.CreateDirectory(NodesDirectory);
            await File.WriteAllTextAsync(PathFor(worldId), json).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Best-effort; the next save overwrites it anyway.
        }
    }

    private string NodesDirectory => _paths.NodesDirectory;

    private string PathFor(ulong worldId) => Path.Combine(NodesDirectory, worldId + ".json");

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
