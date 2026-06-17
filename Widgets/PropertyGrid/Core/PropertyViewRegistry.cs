using Toybox.Studio.Services.World;
using Toybox.Studio.Models.Ecs;
using Toybox.Studio.Services.EngineApi;
using Toybox.Studio.Services.Project;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// Resolves a property's custom editor view (from [[editor::view]] / [View], surfaced as
/// <see cref="PropertyNode.View"/>) to its view-model, before the type-driven fallback in
/// <see cref="PropertyViewModelFactory"/>. View names are matched case-insensitively.
///
/// The custom widgets need app services (the asset catalog, the theme manager) that the static
/// factory can't inject per call, so <see cref="Configure"/> wires them once at startup and the
/// registered builders close over them.
/// </summary>
public static class PropertyViewRegistry
{
    private static AssetCatalog? _assets;
    private static WorldManager? _world;
    private static EngineRpc? _engine;

    /// <summary>
    /// The asset catalog the custom widgets read from. Exposed so the type-driven factory can build a
    /// handle picker directly (handles route by their "handle" type token, not a view name).
    /// </summary>
    public static AssetCatalog? Assets => _assets;

    /// <summary>
    /// The world service the entity picker chooses from (entity-reference fields route by their "entity"
    /// type token).
    /// </summary>
    public static WorldManager? World => _world;

    /// <summary>
    /// The engine RPC the material-instance editor needs to fetch a base material's slots. Exposed so the
    /// type-driven factory can build that editor for any <c>material_instance</c> node (nested or list
    /// element), not just the top-level component.
    /// </summary>
    public static EngineRpc? Engine => _engine;

    private static readonly Dictionary<string, Func<PropertyNode, Action?, PropertyViewModel>> Builders =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["script"] = (node, _) => new ScriptLinkPropertyViewModel(node, _assets),
            ["themePicker"] = (node, commit) => new ThemePickerPropertyViewModel(node, commit),
        };

    /// <summary>
    /// Supplies the services the custom widgets depend on. Called once after the app's services are
    /// built; safe to call again if they are rebuilt.
    /// </summary>
    public static void Configure(AssetCatalog assets, WorldManager world, EngineRpc engine)
    {
        _assets = assets;
        _world = world;
        _engine = engine;
    }

    /// <summary>
    /// Registers a custom view-model builder under a view name (matched case-insensitively), so a node
    /// tagged <c>[[editor::view("name")]]</c> / <c>[View("name")]</c> routes to it. Call at startup to add
    /// a widget without editing this class; its paired View still needs a DataTemplate in
    /// <c>PropertyGridView.axaml</c>.
    /// </summary>
    public static void Register(string view, Func<PropertyNode, Action?, PropertyViewModel> builder) =>
        Builders[view] = builder;

    /// <summary>
    /// Builds the view-model for <paramref name="node"/>'s custom view, or returns false when the node
    /// names no view (or an unregistered one) so the caller falls back to the type-driven widget.
    /// </summary>
    public static bool TryCreate(PropertyNode node, Action? commit, out PropertyViewModel viewModel)
    {
        if (node.View is { Length: > 0 } view && Builders.TryGetValue(view, out var builder))
        {
            viewModel = builder(node, commit);
            return true;
        }

        viewModel = null!;
        return false;
    }
}
