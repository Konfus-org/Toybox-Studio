using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Toybox.Studio.Services.EngineApi;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Services.Project;

/// <summary>
/// A loaded, editable engine asset — the counterpart to the lightweight catalog <see cref="AssetInfo"/>.
/// Obtained from <see cref="AssetCatalog.LoadAsync"/> (an <c>asset.describe</c> snapshot); carries the asset's
/// <see cref="Handle"/>/<see cref="Name"/>/<see cref="Type"/>/<see cref="Path"/>, lets code read/modify its
/// body through the typed asset layer (<see cref="Get{T}"/>/<see cref="Set{T}"/>), and persists it back
/// through <see cref="SaveAsync"/> (the lean <c>asset.save</c>).
///
/// The engine currently describes/saves materials, so this is material-focused today; the shape generalises
/// as the engine gains describe/save for more asset types.
/// </summary>
public sealed class Asset
{
    private readonly EngineRpc _engine;
    private JObject _body;

    internal Asset(EngineRpc engine, AssetInfo info, JObject body)
    {
        _engine = engine;
        Info = info;
        _body = body;
    }

    /// <summary>The catalog metadata this asset was loaded from.</summary>
    public AssetInfo Info { get; }

    /// <summary>A string-free handle to this asset (id + name/type/path).</summary>
    public AssetHandle Handle => new(Info.Id, Info.Name, Info.Type, Info.Path);

    public string Name => Info.Name;

    public string Type => Info.Type;

    public string Path => Info.Path;

    /// <summary>The raw editable asset body (self-describing JSON), for the dynamic/schema-driven path.</summary>
    public JObject Body => _body;

    /// <summary>Reads the asset's body as a typed asset record.</summary>
    public T Get<T>() where T : IAssetType<T> => T.FromAssetJson(_body);

    /// <summary>Stages a modified typed asset body (persisted by <see cref="SaveAsync"/>). Returns this for
    /// fluent use.</summary>
    public Asset Set<T>(T value) where T : IAssetType<T>
    {
        _body = value.ToAssetJson();
        return this;
    }

    /// <summary>Persists the (staged) body back to disk through the engine's lean <c>asset.save</c>.</summary>
    public Task<Result> SaveAsync(CancellationToken ct = default) =>
        _engine.InvokeAsync("asset.save", new { Type, Path, Json = _body }, ct);
}
