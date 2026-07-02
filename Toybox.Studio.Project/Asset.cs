using System.Reflection;
using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Project;
using Toybox.Studio.Project.Assets;
using Toybox.Studio.Utils;

namespace Toybox.Assets;

/// <summary>
/// A handle to one project asset — and the single place GENERIC asset work happens. It carries the asset's
/// identity (<see cref="Handle"/>/<see cref="Name"/>/<see cref="Type"/>/<see cref="Path"/>) and owns the
/// operations that make sense on any asset: lifecycle (<see cref="RenameAsync"/>, <see cref="DeleteAsync"/>,
/// <see cref="DuplicateAsync"/>, <see cref="CopyAsync"/>, <see cref="PasteOverAsync"/>), opening
/// (<see cref="OpenAsync"/>), and — once a body is loaded — <see cref="SaveAsync"/>. Every asset is just data on
/// disk, so any asset can be duplicated by copying its file(s); there is no per-kind "can't duplicate" opt-out.
/// Creation is owned by the <see cref="AssetFactory"/>, not the asset; a kind's per-handle LIFECYCLE quirks are
/// expressed through the virtual hooks here (<see cref="CompanionPaths"/>, <see cref="EditableName"/>,
/// <see cref="RenameTo"/>, <see cref="AffectsBuild"/>).
///
/// An asset doesn't hold global state: the <see cref="AssetFactory"/> (via <see cref="AssetFactory.For(AssetMeta)"/>)
/// builds a handle of the right type and hands it the shared <see cref="AssetServices"/> bundle through its
/// constructor — so the per-handle operations here read their services from that injected bundle rather than a
/// static service locator. The handle then loads/saves itself.
/// </summary>
public abstract class Asset : EngineSyncedObject
{
    // The editor services this handle's operations route through, supplied at construction by the static
    // construction API (the one place assets are built). Null only for a live World, which drives its own engine
    // transport and never uses the asset-services bundle (see the service-less constructor below).
    private readonly AssetServices _services;

    /// <summary>Builds an identity-bound handle wired to the editor services — the way the static construction
    /// API builds every asset. A supplied <paramref name="body"/> hydrates the typed reflected fields.</summary>
    protected Asset(AssetServices services, AssetMeta info, JObject? body = null)
    {
        _services = services;
        Info = info;
        Handle = new AssetHandle(info.Id, info.Name, info.Type, info.Path);
        Body = body;
        // Seed any typed reflected fields (a Material's render type, …) from the loaded body so the typed view
        // matches it; a no-op for an asset (or a bodyless handle) with no reflected fields.
        if (body is not null)
            HydrateFromDescribe(body);
    }

    /// <summary>Service-less constructor for a LIVE World only (active/preview): it carries its own engine
    /// transport and never touches the asset-services bundle — pulling that bundle in would cycle back through
    /// GameState. No other asset uses this.</summary>
    private protected Asset(AssetMeta info) : this(null!, info)
    {
    }

    /// <summary>The asset's identity handle (id + name/type/path) — what a typed field (e.g. a Renderer's
    /// model) stores, and what references this asset. Populated when the handle is created.</summary>
    public AssetHandle Handle { get; private set; }

    // The full catalog metadata behind the handle. Always set immediately after construction via Initialize,
    // before the handle is handed out.
    public AssetMeta Info { get; private set; } = null!;

    /// <summary>The loaded editable body (self-describing JSON), or null until a Load populates it (e.g. a
    /// handle from <see cref="AssetFactory.For(AssetMeta)"/> for a lifecycle op carries no body).</summary>
    public JObject? Body { get; protected set; }

    /// <summary>Whether a body has been loaded (so <see cref="SaveAsync"/> can write it).</summary>
    public bool IsLoaded => Body is not null;

    /// <summary>This asset's strongly-typed payload, or null for an asset with no typed data (a dataless or
    /// import-only kind). A data-bearing asset is an <see cref="Asset{TData}"/>, which narrows this to its
    /// concrete <see cref="AssetData"/> type.</summary>
    public virtual AssetData? Data => null;

    /// <summary>The engine's registered type name a save keys on (e.g. "Material") — derived from the concrete
    /// class name, not stored: a Studio asset class always matches its engine type. A kind whose engine name isn't
    /// its class name (or that saves another way) overrides this or <see cref="SaveAsync"/>.</summary>
    protected virtual string EngineType => GetType().Name;

    /// <summary>The asset's name. Setting it renames the file optimistically (fire-and-forget, like an
    /// <see cref="Entity"/>'s name); use <see cref="RenameAsync(string, CancellationToken)"/> when you need the
    /// <see cref="Result"/>, or <see cref="RenameAsync()"/> for the prompt-driven flow.</summary>
    public string Name
    {
        get => Handle.Name;
        set => RenameAsync(value).FireAndForget();
    }

    public string Type => Handle.Type;

    public string Path => Handle.Path;

    /// <summary>This asset's metadata sidecar (absolute), or its own path when it is self-describing (a
    /// script's <c>.h.meta</c>); null when no project is open. The pairing rules come from the engine via
    /// <see cref="AssetPairing"/>, not hard-coded here.</summary>
    public string? MetadataPath =>
        Projects.CurrentProject is { } project
            ? AssetPairing.MetadataPath(ResolveAbsolute(project, Path))
            : null;

    /// <summary>Every on-disk file this asset comprises (absolute) — the set a delete or move must keep
    /// together. Defaults to the payload + its <c>.meta</c> (see <see cref="CompanionPaths"/>); empty when no
    /// project is open.</summary>
    public IReadOnlyList<string> CompanionFiles =>
        Projects.CurrentProject is { } project ? CompanionPaths(project).ToArray() : [];

    /// <summary>The name shown (and edited) when renaming — the file name without its extension, with any
    /// <c>.meta</c> stripped first so a self-describing asset (a script's <c>.h.meta</c>) reads as its base name.</summary>
    protected virtual string EditableName =>
        System.IO.Path.GetFileNameWithoutExtension(AssetPairing.StripMetadata(Path));

    /// <summary>Whether deleting or renaming this asset must trigger a native rebuild — declared by the asset
    /// kind's <see cref="AssetInfoAttribute.AffectsBuild"/> (scripts). Default false.</summary>
    protected virtual bool AffectsBuild => FileMeta?.AffectsBuild ?? false;

    /// <summary>This asset kind's on-disk metadata declaration (<see cref="AssetInfoAttribute"/>) — read from the
    /// data type for an <see cref="Asset{TData}"/>, else this subclass. Null for a kind that declares none.</summary>
    private AssetInfoAttribute? FileMeta => (Data?.GetType() ?? GetType()).GetCustomAttribute<AssetInfoAttribute>();

    /// <summary>How this asset's payload is encoded (JSON describe body vs raw text), from its
    /// <see cref="AssetInfoAttribute.Format"/>. Default <see cref="AssetFormat.Json"/>.</summary>
    protected AssetFormat Format => FileMeta?.Format ?? AssetFormat.Json;

    /// <summary>The engine connection, for subclasses whose load/save isn't the generic asset.describe/save
    /// path (e.g. project settings, which fetch a schema and write by path).</summary>
    protected Engine Engine => _services.Engine;

    /// <summary>The open project, for subclasses keyed to it rather than the asset catalog (e.g. a script/shader
    /// scaffolding its source under the project tree).</summary>
    protected ProjectManager Projects => _services.Projects;

    /// <summary>The asset catalog, for subclasses that must refresh it after authoring files on disk.</summary>
    protected AssetCatalog Catalog => _services.Catalog;

    /// <summary>The native build, for subclasses whose authoring needs a rebuild (a new script's type).</summary>
    protected ProjectBuilder Builder => _services.Builder;

    /// <summary>User prompts (error / confirm / rename) for the operations that surface failures to the user.</summary>
    protected IUserPrompt Prompt => _services.Prompt;

    /// <summary>Loads this handle's editable body (an <c>asset.describe</c> snapshot) into <see cref="Body"/>,
    /// hydrating the typed reflected fields, then returns itself. <see cref="AssetFactory.For(AssetMeta)"/>
    /// mints the (bodyless) handle; loading its body is the asset's own job. A kind whose body isn't the generic
    /// engine describe (project settings) overrides this. Returns a failure when the engine can't describe it.</summary>
    public virtual async Task<Result<Asset>> LoadAsync(CancellationToken ct = default)
    {
        var result = await _services.Engine
            .SendCommand<JObject>(EngineMethods.AssetDescribe, new { AssetId = Handle.Id }, ct).ContinueOnAnyContext();
        if (result is not { Success: true, Value: { } reply })
            return Result<Asset>.Fail(result.Error ?? "The engine returned no asset.");

        // The editable body lives under the generic "body" key ("material" on the legacy material-only path).
        // Fail rather than smuggle the whole describe envelope (type/typeName/metadata) in as the body — that
        // would be written back verbatim by asset.save.
        if ((reply["body"] ?? reply["material"]) is not JObject body)
            return Result<Asset>.Fail("The engine returned no editable asset body.");

        Body = body;
        HydrateFromDescribe(body);
        return Result<Asset>.Ok(this);
    }

    /// <summary>Persists the (loaded) body back to disk through the engine's lean <c>asset.save</c>, keyed on
    /// the engine's registered type name (not the file extension). A handle with no editable body can't be
    /// saved — import-only kinds (model/script/shader) override this to say so explicitly.</summary>
    public virtual Task<Result> SaveAsync(CancellationToken ct = default)
    {
        if (Body is not { } body)
            return Task.FromResult(Result.Fail($"This {Type} asset has no editable body to save."));

        // Fold any typed reflected edits back into the body before persisting; a no-op for an asset with no
        // reflected fields (which edits its Body directly through the inspector grid).
        WriteSyncedInto(body);
        return _services.Engine.SendCommand(EngineMethods.AssetSave, new { Type = EngineType, Path, Json = body }, ct);
    }

    /// <summary>Opens this asset on the surface that fits its type (code editor / world / asset viewer / OS
    /// default), reusing the open window unless <paramref name="newWindow"/> is set. Call on the UI thread.</summary>
    public Task OpenAsync(bool newWindow = false) => _services.Opener.OpenAsync(Info, newWindow);

    /// <summary>Deletes this asset (all of <see cref="CompanionFiles"/>) after confirmation, then refreshes; a
    /// build-affecting kind also triggers a rebuild.</summary>
    public async Task DeleteAsync()
    {
        if (Projects.CurrentProject is null)
            return;

        var confirmed = await Prompt
            .ConfirmAsync("Delete asset", $"Delete '{Name}'? This removes the file from disk.", "Delete", "Cancel")
            .ContinueOnAnyContext();
        if (!confirmed)
            return;

        try
        {
            foreach (var path in CompanionFiles)
                if (File.Exists(path))
                    File.Delete(path);
        }
        catch (Exception exception)
        {
            await Prompt.ShowErrorAsync("Couldn't delete asset", exception.Message).ContinueOnAnyContext();
            return;
        }

        // The engine lists assets from its in-memory registry, not the disk, and its own file watcher can't
        // re-resolve an already-deleted path to drop it. Now that the file is gone, tell the engine to forget
        // this asset so the refresh below reflects the delete immediately instead of racing the watcher.
        await _services.Engine
            .SendCommand(EngineMethods.AssetForget, new { Id = Handle.Id, Path }, CancellationToken.None)
            .ContinueOnAnyContext();

        // Clear the inspector selection by PATH, not id: the catalog reports many assets with id 0, so an id
        // compare would both miss (a stale id-0 selection of this asset) and over-match (a different id-0 one).
        if (!string.IsNullOrEmpty(Path)
            && string.Equals(_services.Selection.Current.Path, Path, StringComparison.OrdinalIgnoreCase))
            _services.Selection.Clear();

        await Catalog.RefreshAsync().ContinueOnAnyContext();
        if (AffectsBuild)
            Builder.BuildAsync(CancellationToken.None).FireAndForget();
    }

    /// <summary>Renames this asset's files to <paramref name="newName"/> (via <see cref="RenameTo"/>), keeping
    /// its id so references stay stable, then refreshes the catalog and rebuilds for a build-affecting kind.
    /// Returns a failure <see cref="Result"/> on a name collision or with no project open; raises no UI.</summary>
    public async Task<Result> RenameAsync(string newName, CancellationToken ct = default)
    {
        if (Projects.CurrentProject is not { } project)
            return Result.Fail("No project is open.");
        if (string.Equals(newName, EditableName, StringComparison.Ordinal))
            return Result.Ok();

        try
        {
            RenameTo(project, newName);
        }
        catch (Exception exception)
        {
            return Result.Fail(exception.Message);
        }

        await Catalog.RefreshAsync(ct).ContinueOnAnyContext();
        if (AffectsBuild)
            Builder.BuildAsync(CancellationToken.None).FireAndForget();
        return Result.Ok();
    }

    /// <summary>The prompt-driven rename (the Asset Browser's "Rename" / F2): asks for a new name, applies it,
    /// and surfaces any failure as a popup.</summary>
    public async Task RenameAsync()
    {
        var entered = await Prompt
            .PromptForTextAsync("Rename asset", "New name", EditableName, confirmText: "Rename")
            .ContinueOnAnyContext();
        if (entered is null)
            return;

        var result = await RenameAsync(entered).ContinueOnAnyContext();
        if (!result.Success)
            await Prompt.ShowErrorAsync("Couldn't rename asset", result.Error ?? "Unknown error.")
                .ContinueOnAnyContext();
    }

    /// <summary>Duplicates this asset beside itself — copying every file it comprises (see
    /// <see cref="CompanionPaths"/>) to a fresh-id <c>…_copy</c> set — then selects it. Works for any asset, since
    /// an asset is just data on disk.</summary>
    public async Task DuplicateAsync()
    {
        if (Projects.CurrentProject is not { } project)
            return;

        var dataFile = AssetPairing.StripMetadata(ResolveAbsolute(project, Path));
        if (!File.Exists(dataFile))
            return;

        var directory = System.IO.Path.GetDirectoryName(dataFile)!;
        var oldStem = System.IO.Path.GetFileNameWithoutExtension(dataFile);
        var subfolder = System.IO.Path.GetRelativePath(project.RootDirectory, directory).Replace('\\', '/');
        if (subfolder == ".")
            subfolder = string.Empty;
        var (relative, _) = UniquePath(project, subfolder, oldStem, System.IO.Path.GetExtension(dataFile));
        var newStem = System.IO.Path.GetFileNameWithoutExtension(relative);

        var id = await NewAssetIdAsync(_services).ContinueOnAnyContext();
        if (id == 0)
        {
            await Prompt.ShowErrorAsync("Couldn't duplicate asset", "The engine could not mint an asset id.")
                .ContinueOnAnyContext();
            return;
        }

        try
        {
            // Copy every companion (payload + source files) verbatim under the new stem; the identity sidecar gets a
            // fresh id instead so the duplicate is its own asset rather than an alias of the original.
            foreach (var file in CompanionPaths(project))
            {
                if (!File.Exists(file))
                    continue;

                var name = System.IO.Path.GetFileName(file);
                var suffix = name.StartsWith(oldStem, StringComparison.Ordinal) ? name[oldStem.Length..] : name;
                var target = System.IO.Path.Combine(directory, newStem + suffix);
                if (AssetPairing.IsMetadata(file))
                    File.WriteAllText(target, FreshMeta(file, id));
                else
                    File.Copy(file, target);
            }
        }
        catch (Exception exception)
        {
            await Prompt.ShowErrorAsync("Couldn't duplicate asset", exception.Message).ContinueOnAnyContext();
            return;
        }

        await Catalog.RefreshAsync().ContinueOnAnyContext();
        if (AffectsBuild)
            Builder.BuildAsync(CancellationToken.None).FireAndForget();
        SelectByPath(_services, relative);
    }

    /// <summary>Copies this asset's handle to the clipboard for a later paste (a new duplicate, or pasted over an
    /// asset of the same type) — see <see cref="AssetFactory.PasteAsync"/>.</summary>
    public Task CopyAsync() => _services.Clipboard.Copy(Handle);

    /// <summary>Replaces this asset's data with <paramref name="source"/>'s — overwriting this asset's payload from
    /// the copied one while keeping this asset's identity (id/meta), so existing references stay valid — after a
    /// confirmation prompt. The "paste over" half of <see cref="AssetFactory.PasteAsync"/>.</summary>
    public async Task PasteOverAsync(Asset source)
    {
        if (Projects.CurrentProject is not { } project)
            return;

        var confirmed = await Prompt
            .ConfirmAsync("Paste over asset",
                $"Replace the contents of '{Name}' with '{source.Name}'? This can't be undone.",
                "Paste Over", "Cancel")
            .ContinueOnAnyContext();
        if (!confirmed)
            return;

        var sourceData = AssetPairing.StripMetadata(ResolveAbsolute(project, source.Path));
        var targetData = AssetPairing.StripMetadata(ResolveAbsolute(project, Path));
        if (!File.Exists(sourceData))
            return;

        try
        {
            File.Copy(sourceData, targetData, overwrite: true);
        }
        catch (Exception exception)
        {
            await Prompt.ShowErrorAsync("Couldn't paste over asset", exception.Message).ContinueOnAnyContext();
            return;
        }

        await Catalog.RefreshAsync().ContinueOnAnyContext();
        if (AffectsBuild)
            Builder.BuildAsync(CancellationToken.None).FireAndForget();
        SelectByPath(_services, Path);
    }

    /// <summary>Binds this (loaded) asset to a running world it streams into, so reflected-field edits push live to
    /// the engine's resident copy (a <see cref="StreamDestination.Preview"/> world the Asset Viewer keeps it in, or
    /// the <see cref="StreamDestination.Game"/> world the viewports render). Edits still buffer for
    /// <see cref="SaveAsync"/>. The only place live asset reflection is valid, since the engine keeps a streamed
    /// asset resident.</summary>
    internal virtual void BindToStream(StreamDestination destination, IEngineSyncScheduler scheduler) =>
        Bind(EngineAddress.Stream(destination, Handle.Id), scheduler);

    /// <summary>Opens an arbitrary catalog entry on its surface — for a scaffolding kind (script/shader) to open
    /// the source file it just authored, since its create handle has no identity of its own.</summary>
    protected Task OpenAsync(AssetMeta info, bool newWindow = false) => _services.Opener.OpenAsync(info, newWindow);

    /// <summary>The on-disk files this asset comprises (absolute) — the set a delete or move keeps together,
    /// derived generically: the payload, its <c>.meta</c> sidecar, the base file behind a self-describing payload
    /// (a script's <c>.h</c> behind its <c>.h.meta</c>), and each companion declared by
    /// <see cref="AssetInfoAttribute.Companions"/> (a script's <c>.cpp</c>) — by file stem beside the base.</summary>
    protected virtual IEnumerable<string> CompanionPaths(ProjectInfo project)
    {
        var absolute = ResolveAbsolute(project, Path);
        yield return absolute;

        // The .meta sidecar — skipped when the payload IS its own metadata (a self-describing .h.meta).
        var meta = AssetPairing.MetadataPath(absolute);
        if (!string.Equals(meta, absolute, StringComparison.OrdinalIgnoreCase))
            yield return meta;

        // The base file behind a self-describing payload (the .h behind a .h.meta) — its own file otherwise.
        var @base = AssetPairing.StripMetadata(absolute);
        if (!string.Equals(@base, absolute, StringComparison.OrdinalIgnoreCase))
            yield return @base;

        var directory = System.IO.Path.GetDirectoryName(@base)!;
        var stem = System.IO.Path.GetFileNameWithoutExtension(@base);
        foreach (var companion in FileMeta?.Companions ?? [])
            yield return System.IO.Path.Combine(directory, $"{stem}.{companion}");
    }

    /// <summary>Renames every file this asset comprises (see <see cref="CompanionPaths"/>) to
    /// <paramref name="newName"/>, preserving each file's suffix (a script's <c>.h</c>/<c>.cpp</c>/<c>.h.meta</c>
    /// trio renames together). The asset id is kept, so references stay valid. Throws on a name collision.</summary>
    protected virtual void RenameTo(ProjectInfo project, string newName)
    {
        var @base = AssetPairing.StripMetadata(ResolveAbsolute(project, Path));
        var oldStem = System.IO.Path.GetFileNameWithoutExtension(@base);
        if (string.Equals(oldStem, newName, StringComparison.Ordinal))
            return;

        foreach (var file in CompanionPaths(project))
        {
            if (!File.Exists(file))
                continue;

            var directory = System.IO.Path.GetDirectoryName(file)!;
            var name = System.IO.Path.GetFileName(file);
            // Preserve everything after the stem (".h", ".cpp", ".h.meta", ".mat.meta") and swap the stem.
            var suffix = name.StartsWith(oldStem, StringComparison.Ordinal) ? name[oldStem.Length..] : name;
            var target = System.IO.Path.Combine(directory, newName + suffix);
            if (File.Exists(target))
                throw new IOException($"'{newName}' already exists.");

            File.Move(file, target);
        }
    }

    private static async Task<ulong> NewAssetIdAsync(AssetServices services)
    {
        var result = await services.Engine
            .SendCommand<JObject>(EngineMethods.AssetNewId, null, CancellationToken.None).ContinueOnAnyContext();
        return result is { Success: true, Value: { } reply } ? reply.Value<ulong?>("id") ?? 0 : 0;
    }

    /// <summary>The minimal identity-only <c>.meta</c> sidecar JSON (id + version) for a freshly authored asset.</summary>
    protected static string MetaJson(ulong id) => $"{{\n    \"id\": {id},\n    \"version\": 1\n}}\n";

    // A copy of an existing .meta sidecar carrying a fresh id (so a duplicate is its own asset), preserving any other
    // identity fields (a script's polymorphic/type). Falls back to a minimal sidecar if the source can't be read.
    private static string FreshMeta(string sourceMetaPath, ulong id)
    {
        try
        {
            var meta = JObject.Parse(File.ReadAllText(sourceMetaPath));
            meta["id"] = id;
            return meta.ToString() + "\n";
        }
        catch (Exception)
        {
            return MetaJson(id);
        }
    }

    /// <summary>Resolves a project-relative asset path to an absolute one under the project root.</summary>
    protected static string ResolveAbsolute(ProjectInfo project, string relative) =>
        System.IO.Path.IsPathRooted(relative)
            ? relative
            : System.IO.Path.Combine(project.RootDirectory, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));

    /// <summary>A unique (project-relative, forward-slashed) path and its absolute counterpart for a new asset,
    /// appending a <c>_copy</c> suffix to the base name (and again, as needed) until the file doesn't exist.</summary>
    internal static (string relative, string absolute) UniquePath(
        ProjectInfo project, string subfolder, string baseName, string extension)
    {
        var directory = System.IO.Path.Combine(
            project.RootDirectory, subfolder.Replace('/', System.IO.Path.DirectorySeparatorChar));
        var name = baseName;
        var absolute = System.IO.Path.Combine(directory, name + extension);
        while (File.Exists(absolute))
        {
            name += "_copy";
            absolute = System.IO.Path.Combine(directory, name + extension);
        }

        var folder = subfolder.Trim('/');
        var relative = folder.Length == 0 ? $"{name}{extension}" : $"{folder}/{name}{extension}";
        return (relative, absolute);
    }

    private static void SelectByPath(AssetServices services, string relativePath)
    {
        if (services.Catalog.Find(relativePath) is { IsNone: false } handle)
            services.Selection.Select(handle);
    }
}
