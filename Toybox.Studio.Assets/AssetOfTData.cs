using Newtonsoft.Json.Linq;
using Toybox.Assets;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Project.Assets;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Project;

/// <summary>
/// An asset whose editable body is modelled by a strongly-typed <typeparamref name="TData"/> payload — used
/// DIRECTLY (no per-type subclass): the <see cref="AssetFactory"/> builds <c>Asset&lt;Material&gt;</c>,
/// <c>Asset&lt;Texture&gt;</c>, … whose <see cref="Data"/> is the typed payload. The asset owns identity and file
/// lifecycle (inherited from <see cref="Asset"/>); the <typeparamref name="TData"/> owns the engine body's
/// reflected view. Load hydrates the data from <c>asset.describe</c>; Save folds the data's reflected edits back
/// into the body before <c>asset.save</c>.
/// </summary>
public class Asset<TData> : Asset where TData : AssetData, new()
{
    private readonly TData _data = new();

    public Asset(AssetServices services, AssetMeta info, JObject? body = null) : base(services, info, body)
    {
        _data.OnHandleBound(info.Type);
        if (body is not null)
            _data.HydrateFromDescribe(body);
    }

    /// <summary>The strongly-typed payload (the engine body's reflected view), covariantly narrowing the base
    /// <see cref="Asset.Data"/> so callers that know the type get it without a cast.</summary>
    public override TData Data => _data;

    /// <summary>The engine type a save keys on, taken from the DATA type's name (e.g. "Material" for
    /// <c>Asset&lt;Material&gt;</c>) — not the file extension.</summary>
    protected override string EngineType => typeof(TData).Name;

    /// <summary>Loads the editable payload, then returns itself. A JSON asset hydrates <see cref="Data"/> from an
    /// <c>asset.describe</c> snapshot; a plain-text asset (a script/shader source) reads its source file's text
    /// into <see cref="Assets.AssetData.Text"/> — no engine round-trip, no JSON in the asset layer.</summary>
    public override async Task<Result<Asset>> LoadAsync(CancellationToken ct = default)
    {
        if (Format == AssetFormat.PlainText)
            return LoadText();

        var result = await Engine
            .SendCommand<JObject>(EngineMethods.AssetDescribe, new { AssetId = Handle.Id }, ct).ContinueOnAnyContext();
        if (result is not { Success: true, Value: { } reply })
            return Result<Asset>.Fail(result.Error ?? "The engine returned no asset.");

        // The editable body lives under the generic "body" key ("material" on the legacy material-only path).
        if ((reply["body"] ?? reply["material"]) is not JObject body)
            return Result<Asset>.Fail("The engine returned no editable asset body.");

        Body = body;
        _data.HydrateFromDescribe(body);
        return Result<Asset>.Ok(this);
    }

    /// <summary>Persists the payload: a JSON asset folds its typed reflected edits into the body and writes it
    /// through the engine's lean <c>asset.save</c>; a plain-text asset writes <see cref="Assets.AssetData.Text"/>
    /// straight to its source file.</summary>
    public override Task<Result> SaveAsync(CancellationToken ct = default)
    {
        if (Format == AssetFormat.PlainText)
            return Task.FromResult(SaveText());

        if (Body is not { } body)
            return Task.FromResult(Result.Fail($"This {Type} asset has no editable body to save."));

        _data.WriteSyncedInto(body);
        return Engine.SendCommand(EngineMethods.AssetSave, new { Type = EngineType, Path, Json = body }, ct);
    }

    private Result<Asset> LoadText()
    {
        if (TextPath() is not { } path)
            return Result<Asset>.Fail("No project is open.");

        try
        {
            _data.Text = File.ReadAllText(path);
        }
        catch (Exception exception)
        {
            return Result<Asset>.Fail(exception.Message);
        }

        return Result<Asset>.Ok(this);
    }

    private Result SaveText()
    {
        if (TextPath() is not { } path)
            return Result.Fail("No project is open.");

        try
        {
            File.WriteAllText(path, _data.Text ?? string.Empty);
        }
        catch (Exception exception)
        {
            return Result.Fail(exception.Message);
        }

        return Result.Ok();
    }

    // The source file behind a plain-text asset: the base file behind a self-describing payload (a script's .h
    // behind its .h.meta), or the payload path itself otherwise.
    private string? TextPath() =>
        Projects.CurrentProject is { } project
            ? ResolveAbsolute(project, AssetPairing.StripMetadata(Path))
            : null;

    internal override void BindToStream(StreamDestination destination, IEngineSyncScheduler scheduler) =>
        _data.BindToStream(destination, Handle.Id, scheduler);
}
