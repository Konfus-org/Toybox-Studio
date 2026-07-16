using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Utils;

namespace Toybox.Studio.AssetViewer;

/// <summary>
/// The editor-owned asset-preview scene: the bundled <c>AssetPreview.world</c> plus the dependency assets
/// it references by id (the sky textures + sky material, and the world globals that hold the key light and
/// sky). These ship in <c>Resources/AssetViewer</c> (deployed beside the exe and made findable via
/// <c>editor.hello</c>'s <c>add_directory</c>), but the engine's source scan skips build output, so nothing
/// establishes their id→path mapping until something loads them by path. <see cref="LoadAsync"/> registers
/// each dependency (<c>asset.load</c>) — in dependency order so an id reference always resolves — then loads
/// a standalone instance of the world (<c>world.load</c>) whose id references now resolve, and returns its
/// world id. The bridge owns none of this: it just renders whichever world the editor binds its
/// asset-preview view to. Load once per preview view; <see cref="ReleaseAsync"/> (<c>world.close</c>) it
/// when the view goes away.
/// </summary>
public sealed class PreviewScene(Engine engine)
{
    // The world's id-referenced dependencies, in dependency order (textures → material → globals) so each
    // id→path mapping exists before the asset that references it by id is registered/loaded.
    private static readonly string[] DependencyAssets =
        ["SunnySky.png", "DarkSky.png", "Sky.mat", "AssetPreview.globals"];

    private const string WorldPath = "AssetPreview.world";

    /// <summary>Registers the preview world's dependencies, then loads a standalone instance of the preview
    /// world and returns its world id. Fails (without loading the world) if any dependency can't be
    /// registered, so a broken preview surfaces rather than rendering against the red not-found material.</summary>
    public async Task<Result<uint>> LoadAsync()
    {
        foreach (var path in DependencyAssets)
        {
            var registered = await engine
                .SendCommandAsync(EngineCommands.AssetLoad, new { Path = path })
                .ContinueOnAnyContext();
            if (!registered)
                return Result<uint>.Fail($"Couldn't register preview asset '{path}': {registered.Error}");
        }

        var reply = await engine
            .SendCommandAsync<JObject>(EngineCommands.WorldLoad, new { Path = WorldPath })
            .ContinueOnAnyContext();
        if (reply is not { Success: true, Value: { } body })
            return Result<uint>.Fail(reply.Error ?? "world.load returned no preview world id.");

        return Result<uint>.Ok(body.Value<uint?>("worldAssetId") ?? 0);
    }

    /// <summary>Releases the preview world previously loaded by <see cref="LoadAsync"/> (a no-op-safe
    /// world.close on its id).</summary>
    public Task<Result> ReleaseAsync(uint worldId) =>
        engine.SendCommandAsync(EngineCommands.WorldClose, new { WorldAssetId = worldId });
}
