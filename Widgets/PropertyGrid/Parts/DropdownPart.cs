using System.ComponentModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// The disclosure (expand/collapse) part for a composite row. Proxies the owning composite's
/// <see cref="IExpandable.IsExpanded"/> two-way so the chevron toggles the sub-tree, and tracks it if the
/// composite is expanded/collapsed from elsewhere.
/// </summary>
public sealed class DropdownPart : PropertyPart
{
    private readonly IExpandable _owner;

    // Order 10: the chevron sits just right of a list element's drag grip, immediately left of the icon/label.
    public DropdownPart(IExpandable owner) : base(PartSlot.Leading, order: 10)
    {
        _owner = owner;
        _owner.PropertyChanged += OnOwnerChanged;
        ToggleCommand = new RelayCommand(() => IsExpanded = !IsExpanded);
    }

    public bool IsExpanded
    {
        get => _owner.IsExpanded;
        set => _owner.IsExpanded = value;
    }

    /// <summary>Flips the disclosure when the chevron is clicked directly.</summary>
    public ICommand ToggleCommand { get; }

    private void OnOwnerChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(IExpandable.IsExpanded))
            OnPropertyChanged(nameof(IsExpanded));
    }
}
