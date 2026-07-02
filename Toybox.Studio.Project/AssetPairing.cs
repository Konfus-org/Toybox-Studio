using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Project;

/// <summary>
/// How on-disk files pair into one logical asset (metadata sidecars; a C++ header with its implementation), so
/// file operations keep an asset's companions in step. The RULES are the ENGINE's — fetched via
/// <c>asset.pairing</c> and cached here by <see cref="RefreshAsync"/> on connect, so the editor never hard-codes
/// them; the seed values below are only a fallback used before the first fetch (and offline), and the engine
/// overwrites them. The per-path helpers compute from the cached rules.
/// </summary>
public static class AssetPairing
{
    // Engine-authoritative rules, refreshed by RefreshAsync; seeded with the engine's defaults so editor file
    // ops still work before the first connect / offline (the cache persists for the session once fetched).
    private static string _metadataSuffix = ".meta";
    private static IReadOnlyList<string> _sourceHeaderExtensions = [".h", ".hpp", ".hh"];
    private static IReadOnlyList<string> _sourceImplExtensions = [".cpp", ".cc", ".cxx"];

    /// <summary>The sidecar suffix appended to an asset payload to name its metadata file.</summary>
    public static string MetadataSuffix => _metadataSuffix;

    /// <summary>C++ header extensions whose implementations are paired by <see cref="SourceImplExtensions"/>.</summary>
    public static IReadOnlyList<string> SourceHeaderExtensions => _sourceHeaderExtensions;

    /// <summary>Implementation extensions paired with a C++ header (a script's source companions).</summary>
    public static IReadOnlyList<string> SourceImplExtensions => _sourceImplExtensions;

    /// <summary>
    /// Refreshes the pairing rules from the engine (its <c>asset.pairing</c> reply). Called on each connect; the
    /// cached values persist for the session, so editor file ops keep working if the engine later drops. A
    /// failed/absent reply leaves the current (seed or last-fetched) rules in place.
    /// </summary>
    public static async Task RefreshAsync(Engine engine, CancellationToken ct = default)
    {
        var result = await engine.SendCommand<JObject>(EngineMethods.AssetPairing, null, ct).ContinueOnAnyContext();
        if (result is not { Success: true, Value: { } reply })
            return;

        if (reply.Value<string>("metadataSuffix") is { Length: > 0 } suffix)
            _metadataSuffix = suffix;
        if (Strings(reply["sourceHeaderExtensions"]) is { Count: > 0 } headers)
            _sourceHeaderExtensions = headers;
        if (Strings(reply["sourceImplExtensions"]) is { Count: > 0 } impls)
            _sourceImplExtensions = impls;
    }

    /// <summary>True when a path is a self-describing metadata file (a script's <c>&lt;header&gt;.h.meta</c>):
    /// the meta is the asset itself, with no separate payload.</summary>
    public static bool IsSelfDescribingMetadata(string path) =>
        _sourceHeaderExtensions.Any(header =>
            path.EndsWith(header + _metadataSuffix, StringComparison.OrdinalIgnoreCase));

    /// <summary>True when a path ends with the metadata suffix.</summary>
    public static bool IsMetadata(string path) =>
        path.EndsWith(_metadataSuffix, StringComparison.OrdinalIgnoreCase);

    /// <summary>Drops a trailing <c>.meta</c> to recover the file it describes (the asset/header path); returns
    /// the path unchanged when it isn't a metadata file.</summary>
    public static string StripMetadata(string path) =>
        IsMetadata(path) ? path[..^_metadataSuffix.Length] : path;

    /// <summary>The metadata file for an asset payload. A self-describing meta is its own metadata (returned
    /// unchanged); every other asset's metadata lives at <c>&lt;asset&gt;.meta</c>.</summary>
    public static string MetadataPath(string assetPath) =>
        IsSelfDescribingMetadata(assetPath) ? assetPath : assetPath + _metadataSuffix;

    /// <summary>The implementation-source siblings of a header (e.g. <c>Foo.h</c> → <c>Foo.cpp</c>/<c>.cc</c>/
    /// <c>.cxx</c>), by file stem. Existence is the caller's concern — this yields the candidate paths.</summary>
    public static IEnumerable<string> SourceCompanions(string headerPath)
    {
        var directory = Path.GetDirectoryName(headerPath);
        if (directory is null)
            yield break;

        var stem = Path.GetFileNameWithoutExtension(headerPath);
        foreach (var extension in _sourceImplExtensions)
            yield return Path.Combine(directory, stem + extension);
    }

    // Reads a JSON string array into a list, dropping empties; null when the token isn't an array.
    private static IReadOnlyList<string>? Strings(JToken? token) =>
        token is JArray array
            ? array.Select(element => element.Value<string>() ?? "").Where(value => value.Length > 0).ToArray()
            : null;
}
