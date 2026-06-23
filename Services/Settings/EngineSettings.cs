using Newtonsoft.Json.Linq;
using Toybox.Studio.Services.EngineApi;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Services.Settings;

/// <summary>
/// The editor-side construct for the engine's settings surface: fronts the schema-describe and generic
/// asset-save RPCs the Settings panel needs, so that panel depends on this rather than the raw
/// <see cref="EngineRpc"/> transport. The engine owns the authoritative AppSettings schema (and its
/// serializer); this just asks for it and writes a lean document back.
/// </summary>
public sealed class EngineSettings
{
    private readonly EngineRpc _engine;

    public EngineSettings(EngineRpc engine) => _engine = engine;

    /// <summary>Whether the engine is currently connected (the panel falls back to flat file I/O when not).</summary>
    public bool IsConnected => _engine.IsConnected;

    /// <summary>
    /// Fetches the engine's AppSettings schema: a default-constructed AppSettings serialized with reflection
    /// metadata — every field, its engine default, enum choices, and the plugins vector's element_template.
    /// Returns the inner settings field-map (the engine replies <c>{ settings }</c>).
    /// </summary>
    public async Task<Result<JObject>> DescribeSchemaAsync(CancellationToken ct)
    {
        var result = await _engine
            .InvokeAsync<JObject>("app.describeSettings", null, ct)
            .ContinueOnAnyContext();
        return result is { Success: true, Value: { } reply } && reply["settings"] is JObject settings
            ? Result<JObject>.Ok(settings)
            : Result<JObject>.Fail(result.Error ?? "The engine returned no settings schema.");
    }

    /// <summary>
    /// Persists an arbitrary registered asset through the engine: it deserializes <paramref name="json"/> into a
    /// fresh instance of the named type and writes it (lean) to <paramref name="path"/>. The editor's generic
    /// data-panel save path; used by the Settings panel to write AppSettings.json.
    /// </summary>
    public Task<Result> SaveAsync(string type, string path, JObject json, CancellationToken ct) =>
        _engine.InvokeAsync("asset.save", new { Type = type, Path = path, Json = json }, ct);
}
