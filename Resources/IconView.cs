using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Data.Converters;
using Avalonia.Media;
using IconPacks.Avalonia.Lucide.Converter;

namespace Toybox.Studio.Resources;

/// <summary>
/// Renders a Lucide icon by <see cref="Kind"/>, colored by the (inherited) Foreground and scaled to the
/// control's bounds. This is the editor's icon control: the icon pack's own PackIconLucide can't be
/// used — its control theme was compiled against Avalonia 11 and throws MissingMethodException on 12
/// (the DynamicResourceExtension.ProvideValue signature its compiled XAML calls no longer exists) — so
/// this pulls just the icon GEOMETRY out of the pack (pure code, no pack XAML) and draws it itself.
/// </summary>
public sealed class IconView : Control
{
    public static readonly StyledProperty<Icon> KindProperty =
        AvaloniaProperty.Register<IconView, Icon>(nameof(Kind));

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<IconView>();

    // The pack's kind→image converter is the one public door to its path data; the geometry is dug out
    // of the image it builds (whose baked-in brush is ignored — this control draws with its own
    // Foreground) and cached per kind.
    private static readonly IValueConverter GeometrySource = new PackIconLucideKindToImageConverter();
    private static readonly Dictionary<Icon, Geometry?> Geometries = [];

    static IconView()
    {
        AffectsRender<IconView>(KindProperty, ForegroundProperty);
    }

    public Icon Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        if (Kind == Icon.None
            || Foreground is not { } foreground
            || GeometryFor(Kind) is not { } geometry)
        {
            return;
        }

        var ink = geometry.Bounds;
        if (ink.Width <= 0 || ink.Height <= 0 || Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        // Fit the glyph's ink box into the control, preserving aspect, centered.
        var scale = Math.Min(Bounds.Width / ink.Width, Bounds.Height / ink.Height);
        var offsetX = (Bounds.Width - ink.Width * scale) / 2 - ink.X * scale;
        var offsetY = (Bounds.Height - ink.Height * scale) / 2 - ink.Y * scale;
        using var transform = context.PushTransform(new Matrix(scale, 0, 0, scale, offsetX, offsetY));
        context.DrawGeometry(foreground, null, geometry);
    }

    private static Geometry? GeometryFor(Icon kind)
    {
        if (Geometries.TryGetValue(kind, out var cached))
            return cached;

        var geometry = GeometrySource
                .Convert(kind, typeof(DrawingImage), parameter: null, CultureInfo.InvariantCulture) switch
            {
                DrawingImage { Drawing: GeometryDrawing drawing } => drawing.Geometry,
                DrawingImage { Drawing: DrawingGroup group } =>
                    group.Children.OfType<GeometryDrawing>().FirstOrDefault()?.Geometry,
                _ => null,
            };
        return Geometries[kind] = geometry;
    }
}
