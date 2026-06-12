using Toybox.Studio.Services;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// Maps a parsed <see cref="PropertyNode"/> to the view-model for its type — the single place new
/// property widgets get wired in.
/// </summary>
public static class PropertyViewModelFactory
{
    public static PropertyViewModelBase Create(PropertyNode node, Action? commit = null)
    {
        // A read-only field withholds the commit action entirely, so any in-place token edit is never
        // persisted; editable leaf views additionally disable their control via IsReadOnly.
        var effectiveCommit = node.ReadOnly ? null : commit;

        // Nested structs render as a recursive sub-grid regardless of their concrete type token.
        if (node.HasChildren && node.Type != "array")
            return new ObjectPropertyViewModel(node, effectiveCommit);

        // A custom view ([[editor::view]] / [View]) wins over the type-driven widget when registered.
        if (PropertyViewRegistry.TryCreate(node, effectiveCommit, out var custom))
            return custom;

        PropertyViewModelBase viewModel = node.Type switch
        {
            // An enum with declared choices gets a dropdown; without them it falls back to its raw value.
            "enum" when node.Choices is { Count: > 0 } => new EnumPropertyViewModel(node),
            // A handle references an asset, so it's known to be pickable purely from its type token —
            // no [[editor::view]] tag needed. (uuid stays a number: it identifies ids, not assets.)
            "handle" => new HandlePickerPropertyViewModel(node, effectiveCommit, PropertyViewRegistry.Assets),
            "int" or "uuid" or "enum" => new IntPropertyViewModel(node),
            "float" or "double" => new FloatPropertyViewModel(node),
            "bool" => new BoolPropertyViewModel(node),
            "string" => new StringPropertyViewModel(node),
            "vec2" or "vec3" or "vec4" or "quat" => new VectorPropertyViewModel(node),
            "color" => new ColorPropertyViewModel(node),
            "array" => new ArrayPropertyViewModel(node, effectiveCommit),
            _ => new UnknownPropertyViewModel(node),
        };

        viewModel.CommitChanges = effectiveCommit;
        return viewModel;
    }
}
