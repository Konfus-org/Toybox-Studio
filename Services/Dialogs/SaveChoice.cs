namespace Toybox.Studio.Services.Dialogs;

/// <summary>The outcome of an unsaved-changes prompt.</summary>
public enum SaveChoice
{
    /// <summary>Save the unsaved changes, then proceed (close).</summary>
    Save,

    /// <summary>Discard the unsaved changes and proceed (close).</summary>
    Discard,

    /// <summary>Abort — keep things open, don't close.</summary>
    Cancel,
}
