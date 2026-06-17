using System;
using Avalonia;
using Avalonia.Media;
using Newtonsoft.Json;

namespace Toybox.Studio.Services.Theming;

/// <summary>
/// A theme colour expressed as a two-stop gradient between Avalonia <see cref="Color"/> structs. A solid
/// colour is just the degenerate case where <see cref="Start"/> and <see cref="End"/> are equal, so every
/// palette entry can be a flat colour or a gradient without the rest of the system caring which.
/// <see cref="Angle"/> is in degrees (0° = left→right, 90° = top→bottom).
///
/// Persists compactly via <see cref="ColorGradientJsonConverter"/>: a solid round-trips as a plain
/// <c>"#RRGGBB"</c> string (so palettes that stored bare hex still load), and a real gradient as
/// <c>{ "Start": …, "End": …, "Angle": … }</c>. Colours themselves serialise as hex via
/// <see cref="ColorJsonConverter"/>.
/// </summary>
[JsonConverter(typeof(ColorGradientJsonConverter))]
public sealed class ColorGradient
{
    /// <summary>The default gradient angle — a gentle diagonal that reads as soft, modern lighting.</summary>
    public const double DefaultAngle = 135;

    public ColorGradient()
    {
    }

    public ColorGradient(Color start, Color end, double angle = DefaultAngle,
        ColorGradientKind kind = ColorGradientKind.Linear)
    {
        Start = start;
        End = end;
        Angle = angle;
        Kind = kind;
    }

    public Color Start { get; set; } = Colors.Black;

    public Color End { get; set; } = Colors.Black;

    public double Angle { get; set; } = DefaultAngle;

    /// <summary>
    /// Linear (an angled two-stop ramp) or Radial (a soft pool of <see cref="Start"/> in the upper-left that
    /// fades to <see cref="End"/> toward the lower-right — a more organic, less mechanical look).
    /// </summary>
    public ColorGradientKind Kind { get; set; } = ColorGradientKind.Linear;

    /// <summary>True when the two stops match, i.e. this is really a flat colour.</summary>
    [JsonIgnore]
    public bool IsSolid => Start == End;

    /// <summary>
    /// The midpoint of the two stops — used where a single colour must summarise the whole gradient (e.g.
    /// deciding whether a background reads as light, or deriving translucent neutral tints from it).
    /// </summary>
    [JsonIgnore]
    public Color Representative => Blend(Start, End, 0.5f);

    /// <summary>A flat colour as a degenerate gradient (both stops equal, angle irrelevant).</summary>
    public static ColorGradient Solid(Color color) => new(color, color, 0);

    /// <summary>A two-stop linear gradient.</summary>
    public static ColorGradient Linear(Color start, Color end, double angle = DefaultAngle) =>
        new(start, end, angle);

    /// <summary>A radial gradient pooling from the upper-left to the lower-right.</summary>
    public static ColorGradient Radial(Color start, Color end) =>
        new(start, end, DefaultAngle, ColorGradientKind.Radial);

    /// <summary>
    /// Builds the Avalonia brush this gradient describes: a <see cref="SolidColorBrush"/> when the stops
    /// match, a <see cref="RadialGradientBrush"/> for the radial kind, otherwise a
    /// <see cref="LinearGradientBrush"/> whose endpoints are derived from the angle.
    /// </summary>
    public IBrush ToBrush() => BuildBrush(Start, End, Angle, Kind);

    public static IBrush BuildBrush(Color start, Color end, double angle,
        ColorGradientKind kind = ColorGradientKind.Linear)
    {
        if (start == end)
            return new SolidColorBrush(start);

        if (kind == ColorGradientKind.Radial)
        {
            // Pool the start colour in the upper-left, fading to the end colour past the lower-right corner
            // (the unit-square diagonal is ~1.41, so a radius a touch beyond that keeps the falloff smooth).
            return new RadialGradientBrush
            {
                Center = new RelativePoint(0.0, 0.0, RelativeUnit.Relative),
                GradientOrigin = new RelativePoint(0.0, 0.0, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(1.5, RelativeUnit.Relative),
                RadiusY = new RelativeScalar(1.5, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(start, 0),
                    new GradientStop(end, 1),
                },
            };
        }

        var (sx, sy, ex, ey) = EndpointsFor(angle);
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(sx, sy, RelativeUnit.Relative),
            EndPoint = new RelativePoint(ex, ey, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(start, 0),
                new GradientStop(end, 1),
            },
        };
    }

    /// <summary>
    /// Maps a gradient angle (degrees) to start/end points on the unit square, centred at (0.5, 0.5).
    /// 0° runs left→right, 90° top→bottom (Avalonia's Y grows downward).
    /// </summary>
    private static (double sx, double sy, double ex, double ey) EndpointsFor(double angleDegrees)
    {
        var radians = angleDegrees * Math.PI / 180.0;
        var dx = Math.Cos(radians) * 0.5;
        var dy = Math.Sin(radians) * 0.5;
        return (0.5 - dx, 0.5 - dy, 0.5 + dx, 0.5 + dy);
    }

    private static Color Blend(Color a, Color b, float t) => Color.FromArgb(
        (byte)(a.A + (b.A - a.A) * t),
        (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t));
}
