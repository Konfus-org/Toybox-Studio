using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Utils;

namespace Toybox.Studio.AssetViewer;

/// <summary>
/// Drives one asset-preview view's isolated engine world by full sync path. The engine spins that world
/// up per preview view (id returned by <c>view.start</c>); the editor builds the previewed entity in it.
/// The generated <c>Entity</c>/<c>Component</c> mirrors address the active editing world only (and
/// <c>component.set</c> has no engine handler), so this uses the world-qualified structural verbs
/// (<c>entity.create</c> / <c>entity.addComponent</c>) plus <c>sync.set</c> with the full
/// <c>world/{w}/entities/{id}/components/{wire}/{property}</c> path directly. A handle is written as its
/// bare id — the form the engine (de)serializes a <c>Handle</c> field to (its name is not persisted).
/// </summary>
public sealed class PreviewWorld(Engine engine, uint worldAssetId)
{
    /// <summary>Creates an entity in the preview world and returns its id.</summary>
    public async Task<Result<ulong>> CreateEntityAsync(string name, ulong parent = 0)
    {
        var reply = await engine
            .SendCommandAsync<JObject>(
                EngineCommands.EntityCreate,
                new { Name = name, Parent = parent, WorldAssetId = worldAssetId })
            .ContinueOnAnyContext();
        if (reply is not { Success: true, Value: { } body })
            return Result<ulong>.Fail(reply.Error ?? "entity.create returned no id.");

        return Result<ulong>.Ok(body.Value<ulong?>("id") ?? 0);
    }

    /// <summary>Adds a default-constructed component (by its engine snake_case wire name) to the entity.</summary>
    public Task<Result> AddComponentAsync(ulong entityId, string component) =>
        engine.SendCommandAsync(
            EngineCommands.EntityAddComponent,
            new { Component = component, EntityId = entityId, WorldAssetId = worldAssetId });

    /// <summary>Writes one component property by full sync path. The value must already be in the
    /// engine's wire shape (a handle is its bare id; a handle list an array of bare ids).</summary>
    public Task<Result> SetPropertyAsync(ulong entityId, string component, string property, JToken value) =>
        engine.SendCommandAsync(
            EngineCommands.SyncSet,
            new
            {
                // The sync verbs address their target by "address" (the same key EngineObject.CreatePayload
                // uses); the value is the full world-qualified component-property path.
                Address = $"world/{worldAssetId}/entities/{entityId}/components/{component}/{property}",
                Value = value,
            });
}
