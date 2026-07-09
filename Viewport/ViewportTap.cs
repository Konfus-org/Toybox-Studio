namespace Toybox.Studio.Viewport;

/// <summary>
/// One completed viewport tap (a left click that never became a drag), in normalized image
/// coordinates, carrying the selection gesture its held modifiers spell: <see cref="Toggle"/> is the
/// Ctrl-click add-or-remove, <see cref="Additive"/> the Shift-click add — neither clears on a miss.
/// </summary>
public readonly record struct ViewportTap(double U, double V, bool Toggle, bool Additive);
