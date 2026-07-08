using CommunityToolkit.Mvvm.Input;
using Toybox.Studio.PropertyGrid;

namespace Toybox.Studio.Dialogs;

/// <summary>
/// A structured-input popup: a reflection property grid over an arbitrary draft object, resolving to
/// true when confirmed (the caller then reads the edited draft back) or false on cancel/dismiss. The
/// seam for ad-hoc forms — hand it any settings-style object instead of authoring a bespoke dialog.
/// </summary>
public sealed partial class FormPopupViewModel : PopupViewModel<bool>
{
    public FormPopupViewModel(string title, object subject, string confirmText = "OK")
        : base(title)
    {
        Grid = new PropertyGridViewModel(new ReflectionPropertyNodeFactory());
        Grid.Show(subject);
        Buttons =
        [
            new PopupButton("Cancel", CancelCommand, IsCancel: true),
            new PopupButton(confirmText, ConfirmCommand, IsPrimary: true),
        ];
    }

    public PropertyGridViewModel Grid { get; }

    [RelayCommand]
    private void Confirm() => Complete(true);

    [RelayCommand]
    private void Cancel() => Complete(false);
}
