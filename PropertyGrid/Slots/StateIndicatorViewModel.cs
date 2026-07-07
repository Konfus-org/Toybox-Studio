using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Toybox.Studio.PropertyGrid.Slots;

/// <summary>
/// The row's indicator slot, at the trailing edge: a lock when the value is read-only, a filled dot
/// when it differs from its default, a hollow one when it sits at the default. When modified, clicking
/// the dot resets the value to its default (which the shared accessor announces, so the row's editor
/// follows). A row whose accessor knows no default shows nothing (beyond the lock when read-only).
/// </summary>
public sealed partial class StateIndicatorViewModel : ObservableObject
{
    private readonly PropertyValueAccessor _accessor;

    public StateIndicatorViewModel(PropertyValueAccessor accessor)
    {
        _accessor = accessor;
        accessor.Changed += Refresh;
    }

    public bool IsReadOnly => _accessor.IsReadOnly;

    public bool IsDefault => !IsReadOnly && _accessor.HasDefault && _accessor.IsDefault;

    public bool IsModified => !IsReadOnly && _accessor.HasDefault && !_accessor.IsDefault;

    public string Tip =>
        IsReadOnly ? "Read-only"
        : IsModified ? "Modified — click to reset to the default"
        : IsDefault ? "At its default"
        : string.Empty;

    [RelayCommand(CanExecute = nameof(IsModified))]
    private void Reset() => _accessor.Set(_accessor.DefaultValue);

    private void Refresh()
    {
        OnPropertyChanged(nameof(IsDefault));
        OnPropertyChanged(nameof(IsModified));
        OnPropertyChanged(nameof(Tip));
        ResetCommand.NotifyCanExecuteChanged();
    }
}
