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

        // A field whose engine type token has a domain-registered editor (an asset handle, an entity reference,
        // a colour, a material instance) routes to it before any generic widget — including the sub-grid below,
        // so a composite value like a material instance reaches its bespoke editor rather than a plain sub-grid.
        // The editors are registered at startup by the domains that own them (assets, ecs, theming).
        if (PropertyViewRegistry.TryCreateForType(descriptor.Type, descriptor, effectiveAccessor, depth, out var typed))
            return Tag(typed, depth);

        // A custom view ([[editor::view]] / [View] / [ViewModel]) wins over the generic sub-grid below when
        // registered, so a typed field can route a composite value to its bespoke editor rather than a sub-grid.
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
