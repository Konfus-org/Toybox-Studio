using CommunityToolkit.Mvvm.Input;
using IconPacks.Avalonia.Lucide;

namespace Toybox.Studio.PropertyGrid.Slots;

/// <summary>
/// A button in any of a row's slots: one click-invoked action with an icon and tooltip. The generic
/// affordance for factories composing behavior beyond the built-in list slots (browse, reveal,
/// refresh, …).
/// </summary>
public sealed partial class ActionViewModel(PackIconLucideKind icon, string tip, Action action)
{
    public PackIconLucideKind Icon => icon;

    public string Tip => tip;

    [RelayCommand]
    private void Invoke() => action();
}
