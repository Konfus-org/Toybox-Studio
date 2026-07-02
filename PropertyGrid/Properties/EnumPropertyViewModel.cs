namespace Toybox.Studio.PropertyGrid;

/// <summary>
/// Enum property rendered as a dropdown of string choices (from the descriptor's <c>$choices</c>). The commit
/// is carried by the value accessor.
/// </summary>
public sealed class EnumPropertyViewModel : DropdownPropertyViewModel
{
    public EnumPropertyViewModel(PropertyDescriptor descriptor, IValueAccessor accessor)
        : base(descriptor, accessor)
    {
    }
}
