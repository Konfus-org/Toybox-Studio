using System;
using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Project;

/// <summary>
/// One registered script type: its class name and serialization version.
/// </summary>
public sealed record ScriptEntry(string Name, int Version);

/// <summary>
/// The engine's reply to editor.listAssets.
/// </summary>
public sealed record AssetCatalogReply(List<AssetMeta> Assets, List<ScriptEntry> Scripts);

/// <summary>
/// A model asset's real-world stats (the engine's reply to <c>asset.previewStats</c>): its bounding-box size
/// in engine units (metres), triangle total, material-slot count, and mesh count. Dimensions are zero for an
/// asset with no measurable geometry (e.g. a non-model).
/// </summary>
public sealed record AssetPreviewStats(
    double Width, double Height, double Depth, long Triangles, int Materials, int Meshes);

/// <summary>
/// One of a model's hard material slots (an entry in the <c>editor.modelSlots</c> reply): the slot's display
/// <see cref="Name"/> — the source material's name, extracted from the model file at import — and its stable
/// identity <see cref="Id"/>. The Renderer inspector labels its per-slot material pickers from these.
/// </summary>
public sealed record ModelSlot(string Name, ulong Id);

/// <summary>The engine's reply to <c>editor.modelSlots</c>: a model's slots in slot-index order.</summary>
public sealed record ModelSlotsReply(List<ModelSlot> Slots);

/// <summary>
/// The handle database: a UI-ready catalog of the engine's assets and scripts, refreshed on connect, so the
/// property grid can resolve handle/script ids to names and populate asset pickers. It tracks <em>what
/// exists</em> (ids, names, types, paths) and resolves between those — it does not load, edit, or author
/// assets; that is the <see cref="Asset"/> API's job. Mirrors the
/// <see cref="Toybox.Studio.Worlds.GameState"/>'s describe-on-connect pattern.
/// </summary>
public sealed class AssetCatalog : IListenable
{
    /// <summary>
    /// Every concrete DOMAIN <see cref="Asset"/> subclass in this assembly (World, Script, ProjectSettings) that
    /// has the standard <c>(AssetServices, AssetMeta, body)</c> constructor — discovered once by reflection. The
    /// data-bearing kinds are no longer subclasses (they're <c>Asset&lt;TData&gt;</c>, built by the
    /// <see cref="AssetFactory"/>), so the open generic is excluded. The factory reads this to resolve a domain
    /// kind (e.g. a world) from a catalog entry's extension.
    /// </summary>
    public static IReadOnlyList<Type> AssetTypes { get; } =
        typeof(Asset).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false }
                           && typeof(Asset).IsAssignableFrom(type)
                           && type.GetConstructor([typeof(AssetServices), typeof(AssetMeta), typeof(JObject)]) is not null)
            .ToList();

    private readonly Engine _engine;
    private Dictionary<ulong, AssetMeta> _byId = [];

    // Bumped on every connection-state change. A refresh captures it before its RPC and drops the result if
    // the generation moved on (a disconnect or a newer refresh) so a slow reply can't publish over a newer
    // (e.g. empty, post-disconnect) state. All catalog state is published on the UI thread, so the fields are
    // only ever written there and reads from the UI stay consistent.
    private int _generation;

    public AssetCatalog(Session session, Engine engine)
    {
        _engine = engine;
        session.StateChanged += OnSessionStateChanged;
    }

    /// <summary>
    /// Raised on the UI thread after the catalog is (re)published.
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// Raised when a script/asset link is activated (clicked). No-op until a host subscribes.
    /// </summary>
    public event Action<ulong>? AssetActivated;

    public IReadOnlyList<AssetMeta> Assets { get; private set; } = [];

    public IReadOnlyList<ScriptEntry> Scripts { get; private set; } = [];

    /// <summary>
    /// Assets matching any of the given type tokens (case-insensitive); all assets when none are given.
    /// </summary>
    public IReadOnlyList<AssetMeta> AssetsOfType(IReadOnlyList<string>? types)
    {
        if (types is not { Count: > 0 })
            return Assets;

        var wanted = new HashSet<string>(types, StringComparer.OrdinalIgnoreCase);
        return Assets.Where(asset => wanted.Contains(asset.Type)).ToList();
    }

    public AssetMeta? Resolve(ulong id) => _byId.GetValueOrDefault(id);

    public string? ResolveName(ulong id) => Resolve(id)?.Name;

    /// <summary>A string-free <see cref="AssetHandle"/> for an id (carrying its name/type/path), or
    /// <see cref="AssetHandle.None"/> when the id is unknown.</summary>
    public AssetHandle Handle(ulong id) =>
        Resolve(id) is { } asset ? new AssetHandle(asset.Id, asset.Name, asset.Type, asset.Path) : AssetHandle.None;

    /// <summary>Resolves an asset by its project-relative path or display name to an
    /// <see cref="AssetHandle"/> (<see cref="AssetHandle.None"/> when nothing matches). Path wins over name.</summary>
    public AssetHandle Find(string nameOrPath)
    {
        var match = Assets.FirstOrDefault(asset =>
                        string.Equals(asset.Path, nameOrPath, StringComparison.OrdinalIgnoreCase))
                    ?? Assets.FirstOrDefault(asset =>
                        string.Equals(asset.Name, nameOrPath, StringComparison.OrdinalIgnoreCase));
        return match is null ? AssetHandle.None : new AssetHandle(match.Id, match.Name, match.Type, match.Path);
    }

    public void Activate(ulong id) => AssetActivated?.Invoke(id);

    /// <summary>Resolves an asset by display name AND type (e.g. the "Checkerboard" of type "mat", not the
    /// same-named texture) to an <see cref="AssetHandle"/>, or <see cref="AssetHandle.None"/> when none match.</summary>
    public AssetHandle Find(string name, string type)
    {
        var match = Assets.FirstOrDefault(asset =>
            string.Equals(asset.Name, name, StringComparison.OrdinalIgnoreCase)
            && string.Equals(asset.Type, type, StringComparison.OrdinalIgnoreCase));
        return match is null ? AssetHandle.None : new AssetHandle(match.Id, match.Name, match.Type, match.Path);
    }

    /// <summary>
    /// Fetches a model asset's real-world stats (bounding-box size in metres, triangle/material/mesh counts)
    /// for the browser's hover HUD, or null when the engine can't measure it (not a model, not connected).
    /// </summary>
    public async Task<AssetPreviewStats?> PreviewStatsAsync(ulong id, CancellationToken ct = default)
    {
        var result = await _engine
            .SendCommand<AssetPreviewStats>(EngineMethods.AssetPreviewStats, new { AssetId = id }, ct)
            .ContinueOnAnyContext();
        return result is { Success: true, Value: { } stats } ? stats : null;
    }

    /// <summary>
    /// Fetches a model's hard material slots — each carrying the name extracted from the source file at import —
    /// in slot-index order, so the Renderer inspector can label its per-slot material pickers. Empty when the id
    /// isn't a model, the model defines no slots, or the engine can't be reached.
    /// </summary>
    public async Task<IReadOnlyList<ModelSlot>> ModelSlotsAsync(ulong modelId, CancellationToken ct = default)
    {
        if (modelId == 0)
            return [];

        var result = await _engine
            .SendCommand<ModelSlotsReply>(EngineMethods.EditorModelSlots, new { ModelId = modelId }, ct)
            .ContinueOnAnyContext();
        return result is { Success: true, Value: { } reply } ? reply.Slots : [];
    }

    /// <summary>
    /// Asks the engine to write a <c>.meta</c> sidecar for every project asset that lacks one (persisting its
    /// id), then refreshes the catalog. Returns how many were generated.
    /// </summary>
    public async Task<int> GenerateMissingMetasAsync(CancellationToken ct = default)
    {
        var result = await _engine
            .SendCommand<JObject>(EngineMethods.AssetGenerateMissingMetas, null, ct).ContinueOnAnyContext();
        var generated = result is { Success: true, Value: { } reply } ? reply.Value<int?>("generated") ?? 0 : 0;
        await RefreshAsync(ct).ContinueOnAnyContext();
        return generated;
    }

    /// <summary>
    /// Re-fetches the catalog from the engine. Failures surface as an empty catalog.
    /// </summary>
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        // Capture the generation this refresh belongs to; if the connection changes (disconnect, or a newer
        // refresh) before the reply lands, the stale result is dropped instead of clobbering newer state.
        var generation = Volatile.Read(ref _generation);

        // A failure (not connected, disconnect mid-fetch, engine error) surfaces as an empty catalog; the
        // session's disconnect handling owns connection state.
        var result = await _engine
            .SendCommand<AssetCatalogReply>(EngineMethods.EditorListAssets, null, ct).ContinueOnAnyContext();
        var reply = result is { Success: true, Value: { } value } ? value : new AssetCatalogReply([], []);
        PublishIfCurrent(reply, generation);
    }

    private void OnSessionStateChanged(ConnectionState state)
    {
        // Every state change invalidates any in-flight refresh and is the only thing that moves the
        // generation, so the bump and the resulting publish both run on the UI thread for a consistent view.
        Dispatch.To(DispatchContext.UI, () =>
        {
            var generation = ++_generation;
            if (state == ConnectionState.Connected)
                RefreshAsync().FireAndForget();
            else
                Publish(new AssetCatalogReply([], []), generation);
        });
    }

    private void PublishIfCurrent(AssetCatalogReply reply, int generation) =>
        Dispatch.To(DispatchContext.UI, () => Publish(reply, generation));

    private void Publish(AssetCatalogReply reply, int generation)
    {
        // Drop a result whose connection generation has been superseded (a disconnect or newer refresh).
        if (generation != _generation)
            return;

        Assets = reply.Assets;
        Scripts = reply.Scripts;

        // The engine can legitimately report more than one asset with the same id (e.g. id 0 for entries
        // that have no stable id yet), so build the lookup defensively — a plain ToDictionary would throw
        // on the collision and take the whole catalog refresh down. First entry per id wins.
        var byId = new Dictionary<ulong, AssetMeta>(reply.Assets.Count);
        foreach (var asset in reply.Assets)
            byId.TryAdd(asset.Id, asset);
        _byId = byId;

        Changed?.Invoke();
    }
}
