using Avalonia.Media;
using Newtonsoft.Json;

namespace Toybox.Studio.Services.Theming;

/// <summary>
/// Typography for a theme: UI font plus the monospace face used by the console.
/// </summary>
public sealed class ThemeFont
{
    public string Family { get; set; } = "Inter";

    public double Size { get; set; } = 13;

    public string Monospace { get; set; } = "Cascadia Mono";
}

/// <summary>
/// The named colours a theme exposes, each a <see cref="ColorGradient"/> (a flat colour is just a gradient
/// whose two stops match). Brand colours drive accents and read best as gradients, the semantic colours
/// drive log levels and status and are usually flat, and the neutrals drive surfaces and text.
/// The initializer values are the dark default; <see cref="Theme.DefaultLight"/> and
/// <see cref="Theme.DefaultClay"/> override them.
/// </summary>
public sealed class ThemePalette
{
    // Brand — playful gradients.
    public ColorGradient Primary { get; set; } = ColorGradient.Linear(Color.Parse("#7B8CFF"), Color.Parse("#A07BFF"));

    public ColorGradient Secondary { get; set; } = ColorGradient.Solid(Color.Parse("#8893A8"));

    public ColorGradient Tertiary { get; set; } = ColorGradient.Linear(Color.Parse("#3FE0CB"), Color.Parse("#46B6F0"));

    // Semantic — flat, so they map cleanly onto log colours and status dots.
    public ColorGradient Error { get; set; } = ColorGradient.Solid(Color.Parse("#FF7B6E"));

    public ColorGradient Warning { get; set; } = ColorGradient.Solid(Color.Parse("#FFC857"));

    public ColorGradient Info { get; set; } = ColorGradient.Solid(Color.Parse("#5BB8FF"));

    public ColorGradient Success { get; set; } = ColorGradient.Solid(Color.Parse("#4BE08C"));

    // Neutrals — a soft gradient background/surface gives the UI gentle depth.
    public ColorGradient Background { get; set; } = ColorGradient.Radial(Color.Parse("#262B40"), Color.Parse("#171925"));

    public ColorGradient Surface { get; set; } = ColorGradient.Linear(Color.Parse("#2F3450"), Color.Parse("#272B3F"), 90);

    public ColorGradient Text { get; set; } = ColorGradient.Solid(Color.Parse("#EEF1FA"));

    // Buttons — the semantic button fills. These default to the matching brand/semantic/surface colours
    // above, so the out-of-the-box look is unchanged, but each is its own palette entry so a theme can
    // recolour its buttons independently. Action is the brand action fill; Default is the plain (un-classed)
    // button.
    public ColorGradient Action { get; set; } = ColorGradient.Linear(Color.Parse("#7B8CFF"), Color.Parse("#A07BFF"));

    public ColorGradient Play { get; set; } = ColorGradient.Solid(Color.Parse("#4BE08C"));

    public ColorGradient Stop { get; set; } = ColorGradient.Solid(Color.Parse("#FF7B6E"));

    public ColorGradient Refresh { get; set; } = ColorGradient.Solid(Color.Parse("#5BB8FF"));

    public ColorGradient Default { get; set; } = ColorGradient.Linear(Color.Parse("#2F3450"), Color.Parse("#272B3F"), 90);
}

/// <summary>
/// A single, fully self-describing theme as persisted to a Theme.json file under
/// ~/.toybox/Themes. Themes have no light/dark variant of their own: whether a theme reads as light or
/// dark falls out of its colours, and the editor derives the Avalonia base variant from the Background.
/// A user who wants a light/dark pair just authors two themes and names them by convention
/// (e.g. "Toybox Dark" / "Toybox Light"), switching between them like any other theme.
/// </summary>
public sealed class Theme
{
    /// <summary>
    /// Names of the built-in themes written on every run — a playful dark, a playful light, and the
    /// signature clay base. They are non-editable; the Theme Creator always authors a new theme rather than
    /// overwriting one of these.
    /// </summary>
    public const string DarkName = "Toybox Dark";
    public const string LightName = "Toybox Light";
    public const string ClayName = "Claymorphism";

    public string Name { get; set; } = ClayName;

    public double CornerRadius { get; set; } = 6;

    /// <summary>Whether controls and panels cast a soft clay drop shadow.</summary>
    public bool ShadowsEnabled { get; set; } = true;

    /// <summary>
    /// Direction the shadow is cast, in degrees measured clockwise from the +X axis (screen space, where Y
    /// grows downward). 45° = light from the upper-left, shadow toward the lower-right.
    /// </summary>
    public double ShadowAngle { get; set; } = 45;

    public ThemeFont Font { get; set; } = new();

    public ThemePalette Colors { get; set; } = new();

    /// <summary>
    /// True for the shipped defaults, identified by name. Built-ins are read-only.
    /// </summary>
    [JsonIgnore]
    public bool IsBuiltIn =>
        string.Equals(Name, DarkName, StringComparison.OrdinalIgnoreCase)
        || string.Equals(Name, LightName, StringComparison.OrdinalIgnoreCase)
        || string.Equals(Name, ClayName, StringComparison.OrdinalIgnoreCase);

    /// <summary>The built-in themes written to disk on startup, in picker order.</summary>
    public static IReadOnlyList<Theme> BuiltIns => [DefaultClay(), DefaultLight(), DefaultDark()];

    /// <summary>
    /// Claymorphism — our signature base. Warm cream backgrounds, soft pastel clay accents (lavender,
    /// coral, mint), generous rounding, and dark putty text. Reads as a light theme.
    /// </summary>
    public static Theme DefaultClay() => new()
    {
        Name = ClayName,
        CornerRadius = 14,
        Colors = new ThemePalette
        {
            // Diagonal pastel gradients on the brand colours; a near-vertical light→darker bulge on the
            // surfaces reads as a soft clay highlight.
            Primary = ColorGradient.Linear(Color.Parse("#B3A0F5"), Color.Parse("#7E63E0"), 135), // lavender → violet clay
            Secondary = ColorGradient.Solid(Color.Parse("#C7B8A6")),                  // warm putty
            Tertiary = ColorGradient.Linear(Color.Parse("#A6E9C9"), Color.Parse("#6FCE9E"), 135), // mint clay
            Error = ColorGradient.Solid(Color.Parse("#E27D72")),                      // clay coral
            Warning = ColorGradient.Solid(Color.Parse("#F2C266")),                    // butter
            Info = ColorGradient.Solid(Color.Parse("#7FBFE8")),                       // sky
            Success = ColorGradient.Solid(Color.Parse("#86D6AB")),                    // mint
            Background = ColorGradient.Radial(Color.Parse("#FBF4E6"), Color.Parse("#E2CDA4")), // cream pooling to warm tan
            Surface = ColorGradient.Linear(Color.Parse("#FFFCF5"), Color.Parse("#F0E2CB"), 90), // soft clay bulge
            Text = ColorGradient.Solid(Color.Parse("#1C140A")),                       // near-black warm brown
            Action = ColorGradient.Linear(Color.Parse("#B3A0F5"), Color.Parse("#7E63E0"), 135), // brand lavender clay
            Play = ColorGradient.Solid(Color.Parse("#86D6AB")),                       // mint
            Stop = ColorGradient.Solid(Color.Parse("#E27D72")),                       // clay coral
            Refresh = ColorGradient.Solid(Color.Parse("#7FBFE8")),                    // sky
            Default = ColorGradient.Linear(Color.Parse("#FFFCF5"), Color.Parse("#F0E2CB"), 90), // matches surface
        },
    };

    /// <summary>Playful light: cool paper, indigo→violet brand, teal accent.</summary>
    public static Theme DefaultLight() => new()
    {
        Name = LightName,
        CornerRadius = 8,
        Colors = new ThemePalette
        {
            Primary = ColorGradient.Linear(Color.Parse("#6C7CF5"), Color.Parse("#9B6CF5")),
            Secondary = ColorGradient.Solid(Color.Parse("#5B6573")),
            Tertiary = ColorGradient.Linear(Color.Parse("#34D3C0"), Color.Parse("#3BB0E0")),
            Error = ColorGradient.Solid(Color.Parse("#F26B5E")),
            Warning = ColorGradient.Solid(Color.Parse("#F2B705")),
            Info = ColorGradient.Solid(Color.Parse("#3BA7E0")),
            Success = ColorGradient.Solid(Color.Parse("#34C77B")),
            Background = ColorGradient.Radial(Color.Parse("#FAFBFE"), Color.Parse("#E3E9F6")),
            Surface = ColorGradient.Linear(Color.Parse("#FFFFFF"), Color.Parse("#F2F5FC"), 90),
            Text = ColorGradient.Solid(Color.Parse("#28304A")),
            Action = ColorGradient.Linear(Color.Parse("#6C7CF5"), Color.Parse("#9B6CF5")),
            Play = ColorGradient.Solid(Color.Parse("#34C77B")),
            Stop = ColorGradient.Solid(Color.Parse("#F26B5E")),
            Refresh = ColorGradient.Solid(Color.Parse("#3BA7E0")),
            Default = ColorGradient.Linear(Color.Parse("#FFFFFF"), Color.Parse("#F2F5FC"), 90), // matches surface
        },
    };

    /// <summary>Playful dark: soft modern slate (lighter than the old near-black), periwinkle→violet brand.</summary>
    public static Theme DefaultDark() => new()
    {
        Name = DarkName,
        CornerRadius = 10,
        Colors = new ThemePalette(),
    };
}
