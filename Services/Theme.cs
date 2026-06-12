using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Toybox.Studio.Services;

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
/// The named colors a theme exposes, as hex strings (e.g. "#0078D4"). Brand colors drive accents,
/// the semantic colors drive log levels and status, and the neutrals drive surfaces and text.
/// </summary>
public sealed class ThemePalette
{
    public string Primary { get; set; } = "#0078D4";

    public string Secondary { get; set; } = "#6E7B8B";

    public string Tertiary { get; set; } = "#8A63D2";

    public string Error { get; set; } = "#E74C3C";

    public string Warning { get; set; } = "#F1C40F";

    public string Info { get; set; } = "#3498DB";

    public string Success { get; set; } = "#2ECC71";

    public string Background { get; set; } = "#0E1525";

    public string Surface { get; set; } = "#16203A";

    public string Text { get; set; } = "#E8ECF4";
}

/// <summary>
/// A single, fully self-describing theme as persisted to a Theme.json file under
/// ~/.toybox/Themes. Each file is one variant (Dark or Light); the editor pairs a chosen dark
/// theme with a chosen light theme and switches between them.
/// </summary>
public sealed class Theme
{
    /// <summary>
    /// Names of the two built-in themes written on first run. They are non-editable; the Theme Creator
    /// always authors a new theme rather than overwriting one of these.
    /// </summary>
    public const string DarkName = "Toybox Dark";
    public const string LightName = "Toybox Light";

    public string Name { get; set; } = DarkName;

    /// <summary>
    /// Dark or Light — selects the Avalonia base variant this theme applies under. Serialized as its
    /// readable name ("Dark"/"Light"); legacy string files parse back through the same converter.
    /// </summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public ThemeMode Variant { get; set; } = ThemeMode.Dark;

    public double CornerRadius { get; set; } = 6;

    public ThemeFont Font { get; set; } = new();

    public ThemePalette Colors { get; set; } = new();

    [JsonIgnore]
    public bool IsLight => Variant == ThemeMode.Light;

    /// <summary>
    /// True for the two shipped defaults, identified by name. Built-ins are read-only.
    /// </summary>
    [JsonIgnore]
    public bool IsBuiltIn =>
        string.Equals(Name, DarkName, StringComparison.OrdinalIgnoreCase)
        || string.Equals(Name, LightName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The two themes written to disk the first time the editor runs.
    /// </summary>
    public static Theme DefaultDark() => new()
    {
        Name = DarkName,
        Variant = ThemeMode.Dark,
        Colors = new ThemePalette(),
    };

    public static Theme DefaultLight() => new()
    {
        Name = LightName,
        Variant = ThemeMode.Light,
        Colors = new ThemePalette
        {
            Primary = "#0067C0",
            Secondary = "#5B6573",
            Tertiary = "#7A4FC0",
            Error = "#C0392B",
            Warning = "#B7950B",
            Info = "#2471A3",
            Success = "#1E8449",
            Background = "#F4F6FB",
            Surface = "#FFFFFF",
            Text = "#1A2230",
        },
    };
}
