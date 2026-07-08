using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Toybox.Studio.Dialogs;

/// <summary>
/// A pick-one-from-a-list popup (e.g. choosing a saved layout), resolving to the picked
/// <see cref="ListPickItem"/> or null on cancel/dismiss. Double-tapping a row picks it directly.
/// </summary>
public sealed partial class ListPickPopupViewModel : PopupViewModel<ListPickItem?>
{
    public ListPickPopupViewModel(string title, IReadOnlyList<ListPickItem> items, string confirmText = "OK")
        : base(title)
    {
        Items = items;
        Buttons =
        [
            new PopupButton("Cancel", CancelCommand, IsCancel: true),
            new PopupButton(confirmText, PickCommand, IsPrimary: true),
        ];
    }

    public IReadOnlyList<ListPickItem> Items { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PickCommand))]
    public partial ListPickItem? Selected { get; set; }

    [RelayCommand(CanExecute = nameof(CanPick))]
    private void Pick() => Complete(Selected);

    private bool CanPick() => Selected is not null;

    [RelayCommand]
    private void Cancel() => Complete(null);
}
