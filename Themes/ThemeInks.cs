using Avalonia.Media;

namespace Toybox.Studio.Themes;

/// <summary>
/// The values derived once from a <see cref="Theme"/> that the focused theme appliers share: the WCAG
/// auto-contrasted inks (text / on-primary / muted) plus the convenience background and accent colours. Computed
/// in <see cref="ThemeApplier"/> and handed to each applier so the contrast maths runs once, not per section.
/// </summary>
internal readonly record struct ThemeInks(
    Theme Theme,
    Color Background,
    Color Text,
    Color OnPrimary,
    Color Muted,
    Color Accent);
