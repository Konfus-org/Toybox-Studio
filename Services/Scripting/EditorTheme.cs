using Toybox.Studio.Utils;
using Toybox.Studio.Services.Theming;

namespace Toybox.Studio.Services.Scripting;

/// <summary>
/// Picks the Monaco editor's light/dark variant from the active Studio theme's main surface luminance: a dark
/// background gives the dark editor, a light one the light editor. (The accent/Primary colour can't drive this
/// — the light and dark themes share essentially the same periwinkle accent — so the background is used, which
/// also matches how the app derives its own Avalonia light/dark variant.)
/// </summary>
public static class EditorTheme
{
    public static bool IsDark(ThemeManager theme) =>
        Contrast.RelativeLuminance(theme.Active.Colors.Background.Representative) < 0.5;
}
