using System.ComponentModel;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// A composite row (struct/list) that can be expanded or collapsed. Implemented by
/// <c>ObjectPropertyViewModel</c>/<c>ArrayPropertyViewModel</c> so the shared <see cref="DropdownPart"/> can
/// drive the disclosure chevron without knowing the concrete composite type.
/// </summary>
public interface IExpandable : INotifyPropertyChanged
{
    bool IsExpanded { get; set; }
}
