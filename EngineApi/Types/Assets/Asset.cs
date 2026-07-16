using System.Reflection;
using Toybox.Studio.EngineApi.Types;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Logging;
using Toybox.Studio.Utils.Attributes;
using Toybox.Studio.Utils;

namespace Toybox.Studio.EngineApi.Types.Assets;

/// <summary>The engine's create reply: the freshly minted stable id beside the path it wrote.</summary>
internal sealed record AssetCreated(ulong Id, string Path);

/// <summary>
/// One serialized engine asset, mirrored — the family root for every typed asset payload. The
/// class-level [EngineSync] sets the family default (asset edits push through the uniform
/// <see cref="EngineCommands.SyncSet"/>, path-addressed like an entity or component so the edited field
/// rides in the address — <c>asset/{Id}/{property}</c>) and declares the wire address every payload
/// carries. Identity and versioning are engine-owned (they live in the asset's <c>.meta</c>), so both
/// mirror in and never push.
///
/// The whole lifecycle is the constructor plus <see cref="SaveAsync"/>:
/// <c>new Material(id)</c> IS the load — the mirror binds and hydrates itself (await
/// <see cref="Loaded"/> to observe it); <c>new Material("Sky", "Materials")</c> is a fresh asset
/// carrying the location its first save authors it at (gated by the payload's own
/// <see cref="CreatableAttribute"/>). Either way the asset is live — bound to the sync hub, edits
/// pushing per their <see cref="SyncMode"/> and flagging <see cref="IsDirty"/> until saved — and its
/// file identity is <see cref="Name"/>/<see cref="Path"/>, assignable like any other value: the next
/// save renames or moves the file to match. The composition root wires the services all of that needs
/// once, via <see cref="Configure"/>.
/// </summary>
[EngineSync(EngineCommands.SyncSet, Address = "asset/{Id}")]
public abstract partial class Asset
{
    // The services the lifecycle calls need, shared by every asset and wired once at startup —
    // constructors are the API here, so they can't arrive by injection.
    private static SyncHub? _hub;
    private static AssetCatalog? _catalog;
    private static Logger? _log;

    /// <summary>The shared sync hub (and, through it, the engine connection) for asset-scoped engine
    /// queries that aren't property syncs — e.g. a model asking the engine for its material slots. Null
    /// until <see cref="Configure"/> has run. Visible to asset subclasses only.</summary>
    private protected static SyncHub? Hub => _hub;

    // The asset's file location as parts (project-relative directory, name stem, extension without
    // the dot), assignable through Name/Path: a fresh asset's first save creates the file here, and
    // every later save carries the current path so the engine renames/moves the file to match.
    private string _directory = string.Empty;
    private string _name = string.Empty;
    private string _extension = string.Empty;

    // Bumped per bound edit, so a save that raced a new edit knows not to clear the dirty flag.
    private int _edits;

    /// <summary>The load of an existing asset: a real id binds and hydrates the mirror, observable
    /// through <see cref="Loaded"/>. (Id 0 is a blank mirror — a by-value component field, say.)</summary>
    protected Asset(ulong id = 0)
    {
        Id = id;
        Loaded = id == 0 ? Task.FromResult(Result.Ok()) : LoadAsync();
    }

    /// <summary>A fresh asset, authored at the given project-relative location (name uniquified, the
    /// extension from its <see cref="CreatableAttribute"/>) by its first <see cref="SaveAsync"/>.</summary>
    protected Asset(string name, string directory = "")
    {
        _name = name;
        _directory = directory.Trim('/');
        _extension = GetType().GetCustomAttribute<CreatableAttribute>()?.Extension ?? string.Empty;
        Loaded = Task.FromResult(Result.Ok());
    }

    /// <summary>The asset's stable identity, straight from its <c>.meta</c>; engine → studio only.</summary>
    [Hidden]
    [EngineSync(EngineCommands.SyncSet, SyncMode.OneWayFromEngine)]
    public partial ulong Id { get; private set; }

    /// <summary>The serialized payload version, owned by the engine's asset pipeline.</summary>
    [Hidden]
    [EngineSync(EngineCommands.SyncSet, SyncMode.OneWayFromEngine)]
    public partial int Version { get; private set; }

    /// <summary>
    /// The constructor-started load: already completed for a fresh asset, otherwise the bind-and-
    /// hydrate in flight. Await it to know the values are real; anything binding to the mirror instead
    /// just sees them arrive through <c>Changed</c>. A failure is logged, and leaves the asset unbound
    /// on its constructed defaults.
    /// </summary>
    [Hidden]
    public Task<Result> Loaded { get; }

    /// <summary>
    /// Whether the asset carries edits its file doesn't have yet: any bound edit sets it (a live push
    /// reaches only the engine's resident instance — disk is stale until a save), a successful
    /// <see cref="SaveAsync"/> clears it. Unbound assignments (constructor defaults,
    /// template authoring) and inbound engine applies don't count. Flips notify through <c>Changed</c>.
    /// </summary>
    [Hidden]
    public bool IsDirty { get; private set; }

    /// <summary>The asset's file name (its path's stem). Assignable — the file is renamed to match by
    /// the next <see cref="SaveAsync"/>. File identity, not content — kept out of the property grid.</summary>
    [Hidden]
    public string Name
    {
        get => _name;
        set
        {
            if (_name == value)
                return;

            _name = value;
            MarkLocationEdited();
        }
    }

    /// <summary>The asset's project-relative file path. Assignable — the file is moved/renamed to
    /// match by the next <see cref="SaveAsync"/>; a path without an extension keeps the current one.
    /// Empty while a blank mirror has no location. File identity, not content — kept out of the grid.</summary>
    [Hidden]
    public string Path
    {
        get => _name.Length == 0 ? string.Empty : ComposedPath;
        set
        {
            if (Path == value)
                return;

            ReadLocation(value);
            MarkLocationEdited();
        }
    }

    /// <summary>An asset is addressable as a handle by its id, like the engine's
    /// <c>Asset::operator Handle()</c>.</summary>
    public static implicit operator Handle(Asset asset) => new(asset.Id);

    /// <summary>Hands every asset the services its lifecycle sends through; the composition root's one
    /// call, made before any asset is constructed.</summary>
    public static void Configure(SyncHub hub, AssetCatalog catalog, Logger log)
    {
        _hub = hub;
        _catalog = catalog;
        _log = log;
    }

    /// <summary>
    /// Persists the asset, creating it when it doesn't exist yet: a fresh asset (no identity) is
    /// authored by the engine as a new file (plus its <c>.meta</c>) at the constructor-given location,
    /// the current values as its content, and comes back live with its engine-minted identity. An
    /// existing asset's staged edits (batched or manual values still waiting on their window) flush
    /// first, so what lands on disk is what the editor shows; live edits have already pushed through
    /// <see cref="EngineCommands.SyncSet"/> as they were made. The save carries the current
    /// <see cref="Path"/>, so an assigned <see cref="Name"/>/<see cref="Path"/> renames or moves the
    /// file as part of it.
    /// </summary>
    public virtual async Task<Result> SaveAsync(CancellationToken ct = default)
    {
        if (Id == 0)
            return await CreateAsync(ct).ContinueOnAnyContext();

        // An edit racing the save round-trip reaches the engine after the file was written, so the
        // flag only clears when the save covered every edit made so far.
        var edits = _edits;
        var flushed = await CommitAsync(ct).ContinueOnAnyContext();
        if (!flushed)
            return flushed;

        var persisted = await PersistAsync(ct).ContinueOnAnyContext();
        if (persisted && _edits == edits && IsDirty)
        {
            IsDirty = false;
            RaiseChanged();
        }

        return persisted;
    }

    /// <summary>A bound edit is heading for the engine's resident instance, not its file: dirty. The
    /// flip rides the edit's own change notification (this runs just before it). Chaining to the base
    /// raises <see cref="EngineObject.Edited"/>, so a body edit — and only a body edit — feeds the
    /// undo history (a file rename marks dirty through <see cref="MarkEdited"/> without that signal).</summary>
    protected override void OnEdited(EditInfo edit)
    {
        MarkEdited();
        base.OnEdited(edit);
    }

    /// <summary>
    /// Flags a content edit that reached the mirror out of band — a live engine tool (the transform
    /// gizmo) mutated the body through inbound applies, which bypass the setter's edit path and so never
    /// ran <see cref="OnEdited"/>. Marks the asset dirty exactly as a setter edit would (so the change is
    /// saveable) and notifies through <c>Changed</c> so a hosting owner's dirty star tracks it. It does
    /// not re-raise <see cref="EngineObject.Edited"/>: the tool's owner records the single undo step for
    /// the whole interaction itself (see the edit-transaction commit on the asset owner).
    /// </summary>
    public void MarkBodyEdited()
    {
        MarkEdited();
        RaiseChanged();
    }

    // The dirty/edit-counter bookkeeping shared by a synced-body edit and a file-location edit; only the
    // former (through OnEdited) is a content edit that feeds the undo history.
    private void MarkEdited()
    {
        _edits++;
        IsDirty = true;
    }

    [EngineSync(EngineCommands.AssetSave, nameof(Path))]
    private partial Task<Result> PersistAsync(CancellationToken ct);

    // The first save of a fresh asset: the engine authors the file at the constructor-given location
    // and mints the identity that makes the mirror addressable, so it binds live on the way out.
    private async Task<Result> CreateAsync(CancellationToken ct)
    {
        if (_hub is not { } hub || _catalog is not { } catalog)
            return Result.Fail("The asset services are not configured.");
        if (_name.Length == 0)
            return Result.Fail(
                "The asset has no file or location — construct it with a name (and directory) to create it.");
        if (GetType().GetCustomAttribute<CreatableAttribute>() is not { } creatable)
            return Result.Fail($"'{GetType().Name}' assets are imported, not created.");

        if (_extension.Length == 0)
            _extension = creatable.Extension;
        var path = UniquePath(catalog, _directory, _name, _extension);
        var reply = await hub.Engine.SendCommandAsync<AssetCreated>(
                EngineCommands.AssetCreate,
                new { Type = GetType().Name, Path = path, Body = Serialize() },
                ct)
            .ContinueOnAnyContext();
        if (!reply)
            return Result.Fail(reply.Error!);

        // The engine's path is authoritative (the name may have been uniquified on the way in).
        ReadLocation(reply.Value!.Path);
        Id = reply.Value.Id;
        Bind(hub);

        // Best-effort: the engine's registry discovers the new file on its own scan, so the catalog
        // row may land on a later refresh; this asset is authoritative either way.
        await catalog.RefreshAsync(ct).ContinueOnAnyContext();

        return Result.Ok();
    }

    // The constructor-started load: binds (the address needs the id), then hydrates the whole body from
    // the engine's describe over the uniform sync path (sync.describe { address = "asset/{id}" }). A failed
    // hydration unbinds — better an inert mirror on its defaults than one silently presenting them as the
    // asset's state.
    private async Task<Result> LoadAsync()
    {
        // Let construction finish first: the payload constructor assigns its defaults after this base
        // constructor ran, and binding before that would push those assignments as edits.
        await Task.Yield();

        if (_hub is not { } hub)
            return Fault("The asset services are not configured.");

        Bind(hub);
        var hydrated = await RefreshAsync().ContinueOnAnyContext();
        if (!hydrated)
        {
            Unbind();
            return Fault(hydrated.Error!);
        }

        // The file identity comes from the catalog — name and path are file concerns, not body values.
        if (_catalog?.Find(Id) is { } entry)
        {
            ReadLocation(entry.Path);
            RaiseChanged();
        }

        return hydrated;
    }

    // A constructor-load failure may have nobody awaiting Loaded, so it is always logged as well.
    private Result Fault(string error)
    {
        _log?.Warning($"A {GetType().Name} mirror (id {Id:x}) failed to load: {error}");
        return Result.Fail(error);
    }

    // The location parts as one project-relative path; the extension-less form is a blank mirror's.
    private string ComposedPath
    {
        get
        {
            var file = _extension.Length == 0 ? _name : $"{_name}.{_extension}";
            return _directory.Length == 0 ? file : $"{_directory}/{file}";
        }
    }

    // Splits a project-relative path into the location parts; a filename without a dot keeps the
    // current extension (so renames don't need to re-spell it).
    private void ReadLocation(string path)
    {
        var trimmed = path.Trim('/');
        var slash = trimmed.LastIndexOf('/');
        _directory = slash < 0 ? string.Empty : trimmed[..slash];

        var file = slash < 0 ? trimmed : trimmed[(slash + 1)..];
        var dot = file.LastIndexOf('.');
        if (dot > 0)
        {
            _name = file[..dot];
            _extension = file[(dot + 1)..];
        }
        else
        {
            _name = file;
        }
    }

    // A location assignment is an edit like any other — pending until the next save — but only a
    // bound (real) asset can be out of sync with a file.
    private void MarkLocationEdited()
    {
        if (IsBound)
            MarkEdited();
        RaiseChanged();
    }

    // "Name.mat", then "Name 2.mat", "Name 3.mat" — unique against the catalog's known paths. The
    // catalog can lag the disk (a just-created file the registry hasn't scanned), so this is a good
    // default, not a guarantee; the engine's own exists-check is the backstop.
    private static string UniquePath(AssetCatalog catalog, string directory, string name, string extension)
    {
        var folder = directory.Trim('/');
        var taken = new HashSet<string>(
            catalog.Entries.Select(entry => entry.Path), StringComparer.OrdinalIgnoreCase);
        for (var attempt = 1; ; attempt++)
        {
            var stem = attempt == 1 ? name : $"{name} {attempt}";
            var path = folder.Length == 0 ? $"{stem}.{extension}" : $"{folder}/{stem}.{extension}";
            if (!taken.Contains(path))
                return path;
        }
    }
}
