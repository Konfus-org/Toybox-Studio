using Toybox.Studio.Project;

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

    /// <summary>
    /// The asset catalog the custom widgets read from. Exposed so the type-driven factory can build a
    /// handle picker directly (handles route by their "handle" type token, not a view name).
    /// </summary>
    public static AssetCatalog? Assets => _assets;

    private static readonly Dictionary<string, Func<PropertyNode, Action?, PropertyViewModelBase>> Builders =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["script"] = (node, _) => new ScriptLinkPropertyViewModel(node, _assets),
            ["themePicker"] = (node, commit) => new ThemePickerPropertyViewModel(node, commit),
        };

    /// <summary>
    /// Supplies the services the custom widgets depend on. Called once after the app's services are
    /// built; safe to call again if they are rebuilt.
    /// </summary>
    public static void Configure(AssetCatalog assets)
    {
        _assets = assets;
    }

    /// <summary>
    /// Registers a custom view-model builder under a view name (matched case-insensitively), so a node
    /// tagged <c>[[editor::view("name")]]</c> / <c>[View("name")]</c> routes to it. Call at startup to add
    /// a widget without editing this class; its paired View still needs a DataTemplate in
    /// <c>PropertyGridView.axaml</c>.
    /// </summary>
    public static void Register(string view, Func<PropertyNode, Action?, PropertyViewModelBase> builder) =>
        Builders[view] = builder;

    /// <summary>
    /// Builds the view-model for <paramref name="node"/>'s custom view, or returns false when the node
    /// names no view (or an unregistered one) so the caller falls back to the type-driven widget.
    /// </summary>
    public static bool TryCreate(PropertyNode node, Action? commit, out PropertyViewModelBase viewModel)
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
