using System.Linq;
using Newtonsoft.Json.Linq;

namespace Toybox.Studio.Widgets.Settings;

/// <summary>
/// Reconciles the engine's described AppSettings schema with the project's lean on-disk AppSettings.json.
/// The engine describes a default-constructed AppSettings (the full schema, every field's default, and the
/// plugins vector's element_template) in the attributed <c>{ attributes, value }</c> shape; the on-disk file
/// is lean <c>{ type, value }</c> and stores only the fields the project actually overrides. This pairs them:
/// <see cref="Merge"/> overlays the saved values onto a clone of the schema for editing, and
/// <see cref="BuildLean"/> turns the edited schema back into a lean document to persist — writing only the
/// fields that differ from their default, exactly as the engine's own omit-defaults serializer would.
/// </summary>
internal static class AppSettingsDocument
{
    // Unwraps a typed/attributed field to its inner value token; bare values pass through unchanged.
    private static JToken Inner(JToken token) =>
        token is JObject obj && obj.TryGetValue("value", out var value) ? value : token;

    // A schema field's value is a nested struct (recurse field-by-field) when its members are themselves
    // field wrappers — each an object carrying its own "value". A scalar/array/leaf value is not.
    private static bool IsComposite(JToken value) =>
        value is JObject obj && obj.Properties().Any(p => p.Value is JObject child && child.ContainsKey("value"));

    /// <summary>
    /// Overlays the lean on-disk document's values onto <paramref name="schema"/> (a clone of the described
    /// schema) in place. The schema is the authority for structure and defaults; a value present on disk wins,
    /// and everything the file omits keeps its engine default. Keys on disk that the schema doesn't know are
    /// ignored (stale settings drop out).
    /// </summary>
    public static void Merge(JObject schema, JObject disk)
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
    /// whose value differs from <paramref name="defaults"/> (a pristine clone of the described schema). A
    /// nested struct recurses and is written only when at least one descendant differs, so an all-default
    /// subsection is omitted entirely — matching the engine's own omit-defaults output.
    /// </summary>
    public static JObject BuildLean(JObject edited, JObject defaults)
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
}
