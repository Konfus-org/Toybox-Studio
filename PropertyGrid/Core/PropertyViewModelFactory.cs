using Toybox.Studio.EngineApi;

namespace Toybox.Studio.PropertyGrid;

/// <summary>
/// Maps a <see cref="PropertyDescriptor"/> to the view-model for its type, pairing it with the live
/// <see cref="IValueAccessor"/> the widget edits. Type-driven widgets are wired here; custom
/// <c>[[editor::view]]</c> widgets resolve first through <see cref="PropertyViewRegistry"/>. Adding a widget of
/// either kind also means registering its paired View as a DataTemplate in <c>PropertyGridView.axaml</c>.
/// </summary>
public static class PropertyViewModelFactory
{
    public static PropertyViewModel Create(PropertyDescriptor descriptor, IValueAccessor accessor, int depth = 0)
    {
        // A read-only field wraps its accessor so any in-place edit is never persisted; editable leaf views
        // additionally disable their control via IsReadOnly.
        var effectiveAccessor = descriptor.ReadOnly ? new ReadOnlyAccessor(accessor) : accessor;

        // A material instance is edited against its base material's slots (fetched live), so it gets the
        // dedicated base-aware editor instead of a generic sub-grid — anywhere it nests (a field, a list
        // element), provided the engine RPC is wired in to fetch those slots. The top-level material_instance
        // component takes the same path through ComponentViewModel before the grid is ever built.
        if (descriptor.Type == MaterialInstancePropertyViewModel.TypeToken
            && PropertyViewRegistry.Assets is not null
            && MaterialInstancePropertyViewModel.CanBuild(descriptor))
            return Tag(new MaterialInstancePropertyViewModel(descriptor, effectiveAccessor, depth), depth);

        // A custom view ([[editor::view]] / [View] / [ViewModel]) wins over the type-driven widget — and over the
        // generic sub-grid below — when registered, so a typed field can route a composite value (a nested
        // material instance) to its bespoke editor rather than a plain object sub-grid.
        if (PropertyViewRegistry.TryCreate(descriptor, effectiveAccessor, out var custom))
            return Tag(custom, depth);

        // Nested structs render as a recursive sub-grid regardless of their concrete type token.
        if (descriptor.HasChildren && descriptor.Type != EngineTypes.Array)
            return Tag(new ObjectPropertyViewModel(descriptor, effectiveAccessor, depth), depth);

        PropertyViewModel viewModel = descriptor.Type switch
        {
            // An enum with declared choices gets a dropdown; without them it falls back to its raw value.
            EngineTypes.Enum when descriptor.Choices is { Count: > 0 } =>
                new EnumPropertyViewModel(descriptor, effectiveAccessor),
            // A handle references an asset, so it's known to be pickable purely from its type token —
            // no [[editor::view]] tag needed. (uuid stays a number: it identifies ids, not assets.)
            EngineTypes.Handle =>
                new HandlePickerPropertyViewModel(descriptor, effectiveAccessor, PropertyViewRegistry.Assets),
            // An entity-reference field (tbx::Entity) carries the "entity" token; it picks from the world's
            // entities rather than the asset database.
            EngineTypes.Entity =>
                new EntityPickerPropertyViewModel(descriptor, effectiveAccessor, PropertyViewRegistry.World),
            EngineTypes.Int or EngineTypes.Uuid or EngineTypes.Enum =>
                new NumberPropertyViewModel(descriptor, effectiveAccessor, integer: true),
            EngineTypes.Float or EngineTypes.Double =>
                new NumberPropertyViewModel(descriptor, effectiveAccessor, integer: false),
            EngineTypes.Bool => new BoolPropertyViewModel(descriptor, effectiveAccessor),
            EngineTypes.String => new StringPropertyViewModel(descriptor, effectiveAccessor),
            // A quaternion is shown as three Euler-degree fields rather than four raw components.
            EngineTypes.Quat => new RotationPropertyViewModel(descriptor, effectiveAccessor),
            EngineTypes.Vec2 or EngineTypes.Vec3 or EngineTypes.Vec4 =>
                new VectorPropertyViewModel(descriptor, effectiveAccessor),
            EngineTypes.Color => new ColorPropertyViewModel(descriptor, effectiveAccessor),
            EngineTypes.Array => new ArrayPropertyViewModel(descriptor, effectiveAccessor, depth),
            _ => new UnknownPropertyViewModel(descriptor, effectiveAccessor),
        };

        return Tag(viewModel, depth);
    }

    private static PropertyViewModel Tag(PropertyViewModel viewModel, int depth)
    {
        viewModel.Depth = depth;
        return viewModel;
    }
}
