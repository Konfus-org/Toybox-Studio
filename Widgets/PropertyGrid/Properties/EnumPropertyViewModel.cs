using Toybox.Studio.Services.EngineApi;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// Enum property rendered as a dropdown of string choices (from the node's <c>$choices</c>). The commit
/// action is wired by <see cref="PropertyViewModelFactory"/> after construction.
/// </summary>
public sealed class EnumPropertyViewModel : DropdownPropertyViewModel
{
    public EnumPropertyViewModel(PropertyNode node) : base(node)
    {
    }
}
