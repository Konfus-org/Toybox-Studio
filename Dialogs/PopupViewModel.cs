using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Toybox.Studio.Dialogs;

/// <summary>
/// The shape every popup shares: a title, the button row the <see cref="PopupWindow"/> chrome renders
/// beneath the body, and the close signal the <see cref="Popups"/> service wires to the window. The
/// body content is the popup view-model itself — <see cref="PopupViewLocator"/> resolves its view by
/// the <c>XxxViewModel → XxxView</c> convention — so a new popup kind is just a view-model + view pair.
/// </summary>
public abstract class PopupViewModel : ObservableObject
{
    protected PopupViewModel(string title)
    {
        Title = title;
    }

    /// <summary>Raised when the popup wants its host window closed (a button completed it).</summary>
    public event Action? CloseRequested;

    public string Title { get; }

    /// <summary>The button row, left to right; by convention the cancel-ish button leads and the
    /// primary action closes the row.</summary>
    public IReadOnlyList<PopupButton> Buttons { get; protected set; } = [];

    /// <summary>The host window's width (its height sizes to the content).</summary>
    public double Width { get; init; } = 440;

    protected void Close() => CloseRequested?.Invoke();
}

/// <summary>
/// A popup that resolves to a <typeparamref name="TResult"/>. <see cref="Result"/> starts at the
/// popup's dismissal value (what closing the window without choosing means — null for prompts, Cancel
/// for confirms) and is overwritten when a button <see cref="Complete"/>s it.
/// </summary>
public abstract class PopupViewModel<TResult> : PopupViewModel
{
    protected PopupViewModel(string title) : base(title)
    {
    }

    /// <summary>The popup's outcome: the dismissal value until a button completes it.</summary>
    public TResult? Result { get; protected set; }

    /// <summary>Records the outcome and asks the host window to close.</summary>
    protected void Complete(TResult? result)
    {
        Result = result;
        Close();
    }

    /// <summary>A button that completes the popup with a fixed result.</summary>
    protected PopupButton Button(string label, TResult? result, bool isPrimary = false, bool isCancel = false) =>
        new(label, new RelayCommand(() => Complete(result)), isPrimary, isCancel);
}
