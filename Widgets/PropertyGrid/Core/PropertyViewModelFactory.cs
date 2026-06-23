using Toybox.Studio.Services.EngineApi;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// Maps a parsed <see cref="PropertyNode"/> to the view-model for its type. Type-driven widgets are wired
/// here; custom <c>[[editor::view]]</c> widgets resolve first through <see cref="PropertyViewRegistry"/>.
/// Adding a widget of either kind also means registering its paired View as a DataTemplate in
/// <c>PropertyGridView.axaml</c>.
/// </summary>
public static class PropertyViewModelFactory
{
    public static PropertyViewModel Create(PropertyNode node, Action? commit = null, int depth = 0)
    {
        // A read-only field withholds the commit action entirely, so any in-place token edit is never
        // persisted; editable leaf views additionally disable their control via IsReadOnly.
        var effectiveCommit = node.ReadOnly ? null : commit;

        // A material instance is edited against its base material's slots (fetched live), so it gets the
        // dedicated base-aware editor instead of a generic sub-grid — anywhere it nests (a field, a list
        // element), provided the engine RPC is wired in to fetch those slots. The top-level material_instance
        // component takes the same path through ComponentViewModel before the grid is ever built.
        if (node.Type == MaterialInstancePropertyViewModel.TypeToken
            && PropertyViewRegistry.Assets is { } assets
            && MaterialInstancePropertyViewModel.CanBuild(node))
            return Tag(new MaterialInstancePropertyViewModel(node, assets, effectiveCommit, depth), depth);

        // Nested structs render as a recursive sub-grid regardless of their concrete type token.
        if (node.HasChildren && node.Type != "array")
            return Tag(new ObjectPropertyViewModel(node, effectiveCommit, depth), depth);

        // A custom view ([[editor::view]] / [View]) wins over the type-driven widget when registered.
        if (PropertyViewRegistry.TryCreate(node, effectiveCommit, out var custom))
            return Tag(custom, depth);

        PropertyViewModel viewModel = node.Type switch
        {
            // An enum with declared choices gets a dropdown; without them it falls back to its raw value.
            "enum" when node.Choices is { Count: > 0 } => new EnumPropertyViewModel(node),
            // A handle references an asset, so it's known to be pickable purely from its type token —
            // no [[editor::view]] tag needed. (uuid stays a number: it identifies ids, not assets.)
            "handle" => new HandlePickerPropertyViewModel(node, effectiveCommit, PropertyViewRegistry.Assets),
            // An entity-reference field (tbx::Entity) carries the "entity" token; it picks from the world's
            // entities rather than the asset database.
            "entity" => new EntityPickerPropertyViewModel(node, effectiveCommit, PropertyViewRegistry.World),
            "int" or "uuid" or "enum" => new NumberPropertyViewModel(node, integer: true),
            "float" or "double" => new NumberPropertyViewModel(node, integer: false),
            "bool" => new BoolPropertyViewModel(node),
            "string" => new StringPropertyViewModel(node),
            // A quaternion is shown as three Euler-degree fields rather than four raw components.
            "quat" => new RotationPropertyViewModel(node),
            "vec2" or "vec3" or "vec4" => new VectorPropertyViewModel(node),
            "color" => new ColorPropertyViewModel(node),
            "array" => new ArrayPropertyViewModel(node, effectiveCommit, depth),
            _ => new UnknownPropertyViewModel(node),
        };

        viewModel.CommitChanges = effectiveCommit;
        return Tag(viewModel, depth);
    }

    private static PropertyViewModel Tag(PropertyViewModel viewModel, int depth)
    {
        viewModel.Depth = depth;
        return viewModel;
    }
}
