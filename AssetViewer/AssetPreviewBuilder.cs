using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi;
using Toybox.Studio.EngineApi.Types;
using Toybox.Studio.EngineApi.Types.Assets;
using Toybox.Studio.Logging;
using Toybox.Studio.Utils;

namespace Toybox.Studio.AssetViewer;

/// <summary>
/// Builds the previewed entity into an asset-preview world — the shared logic behind the Asset Viewer's
/// editable preview and the Asset Browser's live hover turntable. Given a <see cref="PreviewWorld"/> (a
/// started preview view's world) and the asset to show, it creates one entity with a Transform + Renderer
/// and points the Renderer at the right mesh + materials: a model as-is (any missing material slots filled
/// with the built-in gray matte), a material on a built-in sphere, or a texture (via an engine-built unlit
/// material) on a built-in plane. The caller owns the view lifetime, the loading ghost, and re-framing the
/// camera; it passes an <paramref name="isCurrent"/> guard so a superseded build abandons before the
/// (potentially reused) preview world is written.
/// </summary>
public sealed class AssetPreviewBuilder(Engine engine, AssetCatalog catalog, Logger log)
{
    // The built-in gray matte preview material (Resources/AssetViewer/PreviewMatte.mat), which the engine
    // advertises in the catalog as a built-in asset.
    private const string DefaultMaterialName = "PreviewMatte";

    /// <summary>
    /// Builds the previewed entity into <paramref name="world"/>. Returns the failure of the entity creation
    /// (the one step whose failure means nothing will render); a superseded build (<paramref name="isCurrent"/>
    /// went false after the async resolve) returns success without writing the (now stale) render target.
    /// </summary>
    public async Task<Result> BuildAsync(PreviewWorld world, AssetEntry asset, Func<bool>? isCurrent = null)
    {
        var created = await world.CreateEntityAsync("Preview").ContinueOnAnyContext();
        if (!created)
            return Result.Fail(created.Error!);

        var entity = created.Value;

        // A freshly-created entity has no Transform, and the render pass only draws renderables that have
        // one — add it before the Renderer so the model is actually submitted (and the camera frames it).
        await world.AddComponentAsync(entity, "transform").ContinueOnAnyContext();
        await world.AddComponentAsync(entity, "renderer").ContinueOnAnyContext();

        var (model, materials) = await ResolvePreviewAsync(asset).ContinueOnAnyContext();
        if (isCurrent is not null && !isCurrent())
            return Result.Ok();

        await world.SetPropertyAsync(entity, "renderer", "model", new JValue(model)).ContinueOnAnyContext();
        await world
            .SetPropertyAsync(
                entity, "renderer", "materials", new JArray(materials.Select(id => (object)id).ToArray()))
            .ContinueOnAnyContext();
        return Result.Ok();
    }

    // Resolves the (model, material-overrides) the preview entity's Renderer shows: a model as-is, a
    // material on a built-in preview sphere, or a texture (via an engine-built unlit material) on a plane.
    private async Task<(ulong Model, IReadOnlyList<ulong> Materials)> ResolvePreviewAsync(AssetEntry asset)
    {
        // The built-in preview meshes are advertised through the catalog; ensure it's populated first.
        if (catalog.Entries.Count == 0)
            await catalog.RefreshAsync(CancellationToken.None).ContinueOnAnyContext();

        if (AssetClassifier.IsModel(asset.Type))
            return (asset.Id, await DefaultModelMaterialsAsync(asset.Id).ContinueOnAnyContext());

        if (AssetClassifier.IsTexture(asset.Type))
        {
            var material = await PreviewTextureMaterialAsync(asset.Id).ContinueOnAnyContext();
            return (PreviewMeshId("Plane"), material == 0 ? [] : [material]);
        }

        // A material (or any other previewable) shows on a sphere.
        return (PreviewMeshId("Sphere"), [asset.Id]);
    }

    // The bridge-provided preview-mesh model asset advertised under the given label (0 when absent).
    private ulong PreviewMeshId(string name) =>
        catalog.Entries.FirstOrDefault(entry => entry.Name == name)?.Id ?? 0;

    // The built-in gray matte preview material, matched by its stem against the catalog row's name or path
    // (robust to how the display name/type resolve for a built-in); 0 when it isn't registered.
    private ulong DefaultMaterialId =>
        catalog.Entries.FirstOrDefault(entry =>
                entry.IsBuiltin
                && (string.Equals(entry.Name, DefaultMaterialName, StringComparison.OrdinalIgnoreCase)
                    || entry.Path.StartsWith(
                        DefaultMaterialName + ".", StringComparison.OrdinalIgnoreCase)))
            ?.Id ?? 0;

    // Builds the model preview's per-slot Renderer material overrides: any of the model's material slots
    // that resolve to no material asset are filled with the built-in gray preview material, so a model
    // imported without materials previews gray instead of the red not-found material. The overrides are
    // aligned 1:1 with the model's slots (an empty/inherit handle where the slot already has a material);
    // an empty list when the model has all its materials (or exposes no slots), leaving the model on its
    // own slots. The model's slots mirror in through the standard asset describe (Model.Slots).
    private async Task<IReadOnlyList<ulong>> DefaultModelMaterialsAsync(ulong modelId)
    {
        // Constructing a Model mirror by id IS its load: bind + hydrate the describe body (which now
        // carries the engine's Model::slots). Drop the transient mirror once its slots are read.
        var model = new Model(modelId);
        await model.Loaded.ContinueOnAnyContext();
        var slots = model.Slots ?? [];
        var missing = slots.Select(slot => !HasMaterial(slot)).ToArray();
        model.Unbind();

        if (missing.All(m => !m))
            return []; // every slot already has a material (or the model exposes none) — no overrides

        var gray = DefaultMaterialId;
        if (gray == 0UL)
        {
            log.Warning(
                $"Asset preview: no '{DefaultMaterialName}' preview material is registered; a material-less "
                + "model will preview with the red not-found material.");
            return []; // nothing to fill the missing slots with — leave the model on its own slots
        }

        // A missing slot gets gray; a slot that already has a material inherits (an empty/0 handle).
        return missing.Select(isMissing => isMissing ? gray : 0UL).ToArray();
    }

    // Whether a model slot resolves to a material: its handle id is a material asset the catalog knows.
    private bool HasMaterial(Handle slot) =>
        slot.Id != 0UL
        && catalog.Find(slot.Id) is { } entry
        && AssetClassifier.IsMaterial(entry.Type);

    // Asks the engine for an in-memory unlit material showing the texture flat on a plane; 0 on failure
    // (logged, not surfaced — a missing texture-preview material just shows the plane bare).
    private async Task<ulong> PreviewTextureMaterialAsync(ulong textureId)
    {
        var reply = await engine
            .SendCommandAsync<JObject>(
                EngineCommands.EditorPreviewTextureMaterial, new { TextureId = textureId })
            .ContinueOnAnyContext();
        if (reply is { Success: true, Value: { } body })
            return body.Value<ulong?>("id") ?? 0;

        log.Warning($"Asset preview: couldn't build a preview material for texture {textureId:x}: {reply.Error}");
        return 0;
    }
}
