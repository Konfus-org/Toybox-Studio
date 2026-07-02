using Toybox.Studio.EngineApi;
using Toybox.Studio.Worlds;
using Toybox.Studio.Project;

namespace Toybox.Studio.PropertyGrid;

/// <summary>
/// Resolves a property's custom editor view (from [[editor::view]] / [View], surfaced as
/// <see cref="PropertyDescriptor.View"/>) to its view-model, before the type-driven fallback in
/// <see cref="PropertyViewModelFactory"/>. View names are matched case-insensitively.
///
/// The custom widgets need app services (the asset catalog, the theme manager) that the static
/// factory can't inject per call, so <see cref="Configure"/> wires them once at startup and the
/// registered builders close over them.
/// </summary>
public static class PropertyViewRegistry
{
    private static AssetCatalog? _assets;
    private static AssetFactory? _factory;
    private static GameState? _world;

    // View name → builder. A C# [ViewModel(typeof(X))] field carries X's full type name (the key for typed
    // registrations, added via Register<T>); the remaining short-string keys are the engine/settings
    // [[tbx::view]] names that have no C# attribute to type against (kept until that path is retired). Matched
    // case-insensitively, which is harmless for the exact full names.
    private static readonly Dictionary<string, Func<PropertyDescriptor, IValueAccessor, PropertyViewModel>> Builders =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["script"] = (descriptor, accessor) => new ScriptLinkPropertyViewModel(descriptor, accessor, _assets),
            ["themePicker"] = (descriptor, accessor) => new ThemePickerPropertyViewModel(descriptor, accessor),
            // A material-instance field ([ViewModel(typeof(MaterialInstancePropertyViewModel))]) gets the
            // base-aware override editor when shaped right (material + overrides); otherwise it falls back to a
            // plain sub-grid. Lets a typed component's nested material instance (Sky, PostProcessing) reuse the
            // same editor the describe path's type-token routing gives the top-level material_instance component.
            [typeof(MaterialInstancePropertyViewModel).FullName!] = (descriptor, accessor) =>
                MaterialInstancePropertyViewModel.CanBuild(descriptor)
                    ? new MaterialInstancePropertyViewModel(descriptor, accessor, 0)
                    : new ObjectPropertyViewModel(descriptor, accessor, 0),
        };

    /// <summary>
    /// The asset catalog the custom widgets read from. Exposed so the type-driven factory can build a
    /// handle picker directly (handles route by their "handle" type token, not a view name) and so the
    /// material-instance editor can fetch a base material's slots.
    /// </summary>
    public static AssetCatalog? Assets => _assets;

    /// <summary>
    /// The asset factory the material-instance override editor uses to load a base material's body (it isn't
    /// DI-constructed — the grid builds it deep in a static factory — so it reaches the factory here).
    /// </summary>
    public static AssetFactory? Factory => _factory;

    /// <summary>
    /// The game state the entity picker chooses from (entity-reference fields route by their "entity" type
    /// token, listing the active world's entities).
    /// </summary>
    public static GameState? World => _world;

    /// <summary>
    /// Supplies the services the custom widgets depend on. Called once after the app's services are
    /// built; safe to call again if they are rebuilt.
    /// </summary>
    public static void Configure(AssetCatalog assets, AssetFactory factory, GameState world)
    {
        _assets = assets;
        _factory = factory;
        _world = world;
    }

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
