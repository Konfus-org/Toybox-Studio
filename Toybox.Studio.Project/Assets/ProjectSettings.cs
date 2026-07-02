using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using Toybox.Assets;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Project.Assets;

/// <summary>
/// The open project's settings asset — its lean <c>AppSettings.json</c> (engine asset type <c>AppSettings</c>).
/// Unlike a catalog asset, it isn't described/saved the generic way: when the engine is connected its editable
/// <see cref="Asset.Body"/> is the engine-described schema (graphics, physics, the resizable plugins list, …)
/// with the project's saved overrides merged in, and <see cref="SaveAsync"/> writes a lean document (only the
/// fields that differ from the engine defaults), exactly as the engine's own omit-defaults serializer would.
/// With no engine it falls back to editing the flat file. This is the home of the schema ↔ lean-file
/// reconciliation the Project settings grid edits through.
/// </summary>
public sealed class ProjectSettings : Asset
{
    private const string AppSettingsType = "AppSettings";

    // The pristine described schema (attributed defaults) the lean save diffs against; null on the flat
    // fallback path (engine not connected), where Body is the on-disk document as-is.
    private JObject? _defaults;

    public ProjectSettings(AssetServices services, AssetMeta info, JObject? body = null)
        : base(services, info, body)
    {
    }

    /// <summary>
    /// Loads the current project's settings into this handle's <see cref="Asset.Body"/>: reads the lean
    /// <c>AppSettings.json</c> and, when the engine is connected, overlays it onto the described schema for
    /// editing. Overrides the generic <c>asset.describe</c> load with the AppSettings schema-merge.
    /// </summary>
    public override async Task<Result<Asset>> LoadAsync(CancellationToken ct = default)
    {
        if (Projects.CurrentProject is not { } project)
            return Result<Asset>.Fail("No project is open.");

        var app = ReadAppJson(project.AppSettingsPath) ?? new JObject();

        JObject document = app;
        if (Engine.IsConnected && await DescribeSchemaAsync(ct).ContinueOnAnyContext() is { } schema)
        {
            _defaults = (JObject)schema.DeepClone();
            document = (JObject)schema.DeepClone();
            Merge(document, app);
        }

        Body = document;
        return Result<Asset>.Ok(this);
    }

    /// <summary>
    /// Persists the edited settings as a lean document (only fields differing from the engine defaults) through
    /// the engine's generic asset save when connected, else writes the flat file directly so settings stay
    /// editable with no running engine. The running engine hot-reloads AppSettings on its own, so this only
    /// refreshes the project display name (which the "name" field can change) — it does NOT reopen the project.
    /// </summary>
    public override async Task<Result> SaveAsync(CancellationToken ct = default)
    {
        if (Projects.CurrentProject is not { } project)
            return Result.Fail("No project is open.");
        if (Body is not { } body)
            return Result.Fail("Project settings have no loaded body to save.");

        var toWrite = _defaults is { } defaults ? BuildLean(body, defaults) : body;

        if (Engine.IsConnected)
        {
            var result = await Engine
                .SendCommand(EngineMethods.AssetSave, new { Type = AppSettingsType, Path = project.AppSettingsPath, Json = toWrite }, ct)
                .ContinueOnAnyContext();
            if (!result.Success)
                return result;
        }
        else
        {
            try
            {
                await File.WriteAllTextAsync(project.AppSettingsPath, toWrite.ToString(Formatting.Indented), ct)
                    .ContinueOnAnyContext();
            }
            catch (Exception exception)
            {
                return Result.Fail(exception.Message);
            }
        }

        Projects.RefreshDisplayName();
        return Result.Ok();
    }

    // Fetches the engine's AppSettings schema (a default-constructed AppSettings serialized with reflection
    // metadata), or null when it can't be obtained. Returns the inner settings field-map.
    private async Task<JObject?> DescribeSchemaAsync(CancellationToken ct)
    {
        var result = await Engine
            .SendCommand<JObject>(EngineMethods.AppDescribeSettings, null, ct).ContinueOnAnyContext();
        return result is { Success: true, Value: { } reply } && reply["settings"] is JObject settings
            ? settings
            : null;
    }

    private static JObject? ReadAppJson(string path)
    {
        try
        {
            return JObject.Parse(File.ReadAllText(path));
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Overlays the lean on-disk document's values onto <paramref name="schema"/> (a clone of the described
    /// schema) in place. The schema is the authority for structure and defaults; a value present on disk wins,
    /// and everything the file omits keeps its engine default. Keys on disk the schema doesn't know are ignored.
    /// </summary>
    private static void Merge(JObject schema, JObject disk)
    {
        foreach (var diskField in disk.Properties())
            if (schema[diskField.Name] is JObject schemaField)
                MergeField(schemaField, Inner(diskField.Value));
    }

    private static void MergeField(JObject schemaField, JToken diskValue)
    {
        var schemaValue = schemaField["value"];
        if (schemaValue is JObject nested && IsComposite(nested) && diskValue is JObject diskObject)
        {
            foreach (var diskChild in diskObject.Properties())
                if (nested[diskChild.Name] is JObject childField)
                    MergeField(childField, Inner(diskChild.Value));
        }
        else
        {
            // Leaf or array — the disk value replaces the default wholesale (the field's attributes,
            // including the array element_template, are left intact).
            schemaField["value"] = diskValue.DeepClone();
        }
    }

    /// <summary>
    /// Builds the lean <c>{ type, value }</c> document to persist from the edited schema, emitting only fields
    /// whose value differs from <paramref name="defaults"/> (a pristine clone of the described schema). A nested
    /// struct recurses and is written only when at least one descendant differs, so an all-default subsection is
    /// omitted entirely — matching the engine's own omit-defaults output.
    /// </summary>
    private static JObject BuildLean(JObject edited, JObject defaults)
    {
        var lean = new JObject();
        foreach (var field in edited.Properties())
            if (defaults[field.Name] is JObject defaultField && field.Value is JObject editedField
                && LeanField(editedField, defaultField) is { } leanField)
                lean[field.Name] = leanField;

        return lean;
    }

    private static JObject? LeanField(JObject editedField, JObject defaultField)
    {
        // The lean shape carries the type token at the top level; the described field keeps it under
        // "attributes". (The bare-value fallback path has neither, leaving type null — harmless, since the
        // engine reads each value by its static field type and treats the token as cosmetic.)
        var type = (editedField["attributes"]?["type"] ?? editedField["type"])?.DeepClone();
        var editedValue = editedField["value"];
        var defaultValue = defaultField["value"];

        if (editedValue is JObject nested && IsComposite(nested) && defaultValue is JObject defaultNested)
        {
            var leanChildren = new JObject();
            foreach (var child in nested.Properties())
                if (defaultNested[child.Name] is JObject defaultChild && child.Value is JObject childField
                    && LeanField(childField, defaultChild) is { } leanChild)
                    leanChildren[child.Name] = leanChild;

            return leanChildren.Count == 0
                ? null
                : new JObject { ["type"] = type, ["value"] = leanChildren };
        }

        return JToken.DeepEquals(editedValue, defaultValue)
            ? null
            : new JObject { ["type"] = type, ["value"] = editedValue?.DeepClone() };
    }

    // Unwraps a typed/attributed field to its inner value token; bare values pass through unchanged.
    private static JToken Inner(JToken token) =>
        token is JObject obj && obj.TryGetValue("value", out var value) ? value : token;

    // A schema field's value is a nested struct (recurse field-by-field) when its members are themselves field
    // wrappers — each an object carrying its own "value". A scalar/array/leaf value is not.
    private static bool IsComposite(JToken value) =>
        value is JObject obj && obj.Properties().Any(p => p.Value is JObject child && child.ContainsKey("value"));
}
