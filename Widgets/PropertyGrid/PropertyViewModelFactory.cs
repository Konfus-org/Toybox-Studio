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
        // Nested structs render as a recursive sub-grid regardless of their concrete type token.
        if (node.HasChildren && node.Type != "array")
            return new ObjectPropertyViewModel(node, commit);

        PropertyViewModelBase viewModel = node.Type switch
        {
            // An enum with declared choices gets a dropdown; without them it falls back to its raw value.
            "enum" when node.Choices is { Count: > 0 } => new EnumPropertyViewModel(node),
            "int" or "uuid" or "handle" or "enum" => new IntPropertyViewModel(node),
            "float" or "double" => new FloatPropertyViewModel(node),
            "bool" => new BoolPropertyViewModel(node),
            "string" => new StringPropertyViewModel(node),
            "vec2" or "vec3" or "vec4" or "quat" => new VectorPropertyViewModel(node),
            "color" => new ColorPropertyViewModel(node),
            "array" => new ArrayPropertyViewModel(node, commit),
            _ => new UnknownPropertyViewModel(node),
        };

        viewModel.CommitChanges = commit;
        return viewModel;
    }
}
