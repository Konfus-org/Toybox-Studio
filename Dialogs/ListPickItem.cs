namespace Toybox.Studio.Dialogs;

/// <summary>One pickable row in a <see cref="ListPickPopupViewModel"/>: a stable key, the display
/// name, and an optional dimmer detail line.</summary>
public sealed record ListPickItem(string Key, string Name, string Detail = "");
