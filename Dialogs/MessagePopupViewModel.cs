namespace Toybox.Studio.Dialogs;

/// <summary>
/// A one-way message (info / warning / error): a severity icon beside wrapped text, closed by OK.
/// Backs the <see cref="Popups"/> message presets.
/// </summary>
public sealed class MessagePopupViewModel : PopupViewModel<bool>
{
    public MessagePopupViewModel(string title, string message, MessageSeverity severity)
        : base(title)
    {
        Message = message;
        Severity = severity;
        Buttons = [Button("OK", true, isPrimary: true)];
    }

    public string Message { get; }

    public MessageSeverity Severity { get; }

    // Per-severity flags so the view's three pre-styled icons can toggle with plain compiled bindings.
    public bool IsInfo => Severity == MessageSeverity.Info;

    public bool IsWarning => Severity == MessageSeverity.Warning;

    public bool IsError => Severity == MessageSeverity.Error;
}
