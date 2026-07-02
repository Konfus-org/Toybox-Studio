namespace Toybox.Studio.PropertyGrid;

/// <summary>
/// Resolves a property's custom editor view (from [[editor::view]] / [View], surfaced as
/// <see cref="PropertyDescriptor.View"/>) to its view-model, before the type-driven fallback in
/// <see cref="PropertyViewModelFactory"/>. View names are matched case-insensitively.
///
/// Purely a registry: domain projects register their editors (by view name or engine type token) at
/// startup, so the generic grid core holds no reference to any domain-specific editor or service. The
/// asset services those editors need live in the asset layer's <c>AssetGridServices</c>, not here.
/// </summary>
public static class PropertyViewRegistry
{

    // View name → builder. A C# [ViewModel(typeof(X))] field carries X's full type name (the key for typed
    // registrations, added via Register<T>); the remaining short-string keys are the engine/settings
    // [[tbx::view]] names that have no C# attribute to type against (kept until that path is retired). Matched
    // case-insensitively, which is harmless for the exact full names. Populated at startup by the owning domain
    // projects (nothing is hard-coded here, so the grid core stays free of domain-editor references).
    private static readonly Dictionary<string, Func<PropertyDescriptor, IValueAccessor, PropertyViewModel>> Builders =
        new(StringComparer.OrdinalIgnoreCase);

    // Engine type token (EngineTypes.Handle/Entity/Color, a component's material_instance token, …) → builder.
    // These route purely by a field's type — no [View] tag needed — and are registered at startup by the domain
    // that owns the editor (assets, ecs, theming), so the generic grid never references those editors directly.
    private static readonly Dictionary<string, Func<PropertyDescriptor, IValueAccessor, int, PropertyViewModel>> TypeBuilders =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The asset/entity chooser the picker editors open, supplied by the dialog layer at startup so the
    /// grid stays free of a dialog dependency. Null until wired.</summary>
    public static IAssetPicker? AssetPicker { get; set; }

    /// <summary>
    /// Registers a custom view-model builder for the view-model type <typeparamref name="TViewModel"/>, so a
    /// member tagged <c>[ViewModel(typeof(TViewModel))]</c> (which carries the type's full name) routes to it.
    /// Call at startup to add a widget without editing this class; its paired View still needs a DataTemplate in
    /// <c>PropertyGridView.axaml</c>.
    /// </summary>
    public static void Register<TViewModel>(Func<PropertyDescriptor, IValueAccessor, PropertyViewModel> builder)
        where TViewModel : PropertyViewModel =>
        Builders[typeof(TViewModel).FullName!] = builder;

    /// <summary>
    /// Registers a builder under a legacy string view name — for the engine/settings <c>[[tbx::view("name")]]</c>
    /// path that has no C# view-model type to key against (e.g. the settings <c>intensitySlider</c>). Prefer
    /// <see cref="Register{TViewModel}"/> for C#-authored views.
    /// </summary>
    public static void Register(string view, Func<PropertyDescriptor, IValueAccessor, PropertyViewModel> builder) =>
        Builders[view] = builder;

    /// <summary>
    /// Registers a builder for an engine type token (e.g. <c>EngineTypes.Handle</c>, <c>EngineTypes.Entity</c>,
    /// a component's <c>material_instance</c> token). Fields of that type route to this editor before the generic
    /// type-driven widgets, without needing a <c>[View]</c> tag. Called at startup by the domain that owns the
    /// editor, so the grid core never references it. The builder receives the nesting depth.
    /// </summary>
    public static void RegisterType(
        string typeToken, Func<PropertyDescriptor, IValueAccessor, int, PropertyViewModel> builder) =>
        TypeBuilders[typeToken] = builder;

    /// <summary>
    /// Builds the editor a registered <see cref="RegisterType"/> token maps to, or returns false when the
    /// descriptor's type has no registered editor so the caller falls back to the generic type-driven widget.
    /// </summary>
    public static bool TryCreateForType(
        string typeToken, PropertyDescriptor descriptor, IValueAccessor accessor, int depth,
        out PropertyViewModel viewModel)
    {
        if (TypeBuilders.TryGetValue(typeToken, out var builder))
        {
            viewModel = builder(descriptor, accessor, depth);
            return true;
        }

        viewModel = null!;
        return false;
    }

    /// <summary>
    /// Builds the view-model for <paramref name="descriptor"/>'s custom view, or returns false when it
    /// names no view (or an unregistered one) so the caller falls back to the type-driven widget.
    /// </summary>
    public static bool TryCreate(
        PropertyDescriptor descriptor, IValueAccessor accessor, out PropertyViewModel viewModel)
    {
        if (descriptor.View is { Length: > 0 } view && Builders.TryGetValue(view, out var builder))
        {
            viewModel = builder(descriptor, accessor);
            return true;
        }

        viewModel = null!;
        return false;
    }
}
