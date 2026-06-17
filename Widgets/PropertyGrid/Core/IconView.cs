using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using IconPacks.Avalonia.Lucide;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// Renders the editor icon a type advertises through its [[tbx::icon("Name", Color::X)]] attribute.
/// <see cref="IconName"/> is resolved against the Lucide icon set (case-insensitive); an unknown or empty
/// name simply hides the glyph rather than failing, so author typos degrade gracefully. <see cref="IconColor"/>
/// is a tbx Color constant name (e.g. "BLUE") mapped to an accent brush; an unknown colour inherits.
/// </summary>
public sealed class IconView : PackIconLucide
{
    // tbx::Color constants → display brushes. Kept in sync with tbx/types/color.h's named constants.
    private static readonly Dictionary<string, Color> Palette = new(StringComparer.OrdinalIgnoreCase)
    {
        ["WHITE"] = Color.Parse("#FFFFFF"),
        ["BLACK"] = Color.Parse("#000000"),
        ["RED"] = Color.Parse("#E24B4A"),
        ["GREEN"] = Color.Parse("#639922"),
        ["BLUE"] = Color.Parse("#378ADD"),
        ["YELLOW"] = Color.Parse("#E8B53A"),
        ["CYAN"] = Color.Parse("#1D9E75"),
        ["MAGENTA"] = Color.Parse("#D4537E"),
        ["GREY"] = Color.Parse("#888780"),
        ["LIGHT_GREY"] = Color.Parse("#B4B2A9"),
        ["DARK_GREY"] = Color.Parse("#5F5E5A"),
    };

    public static readonly StyledProperty<string?> IconNameProperty =
        AvaloniaProperty.Register<IconView, string?>(nameof(IconName));

    public static readonly StyledProperty<string?> IconColorProperty =
        AvaloniaProperty.Register<IconView, string?>(nameof(IconColor));

    public string? IconName
    {
        get => GetValue(IconNameProperty);
        set => SetValue(IconNameProperty, value);
    }

    public string? IconColor
    {
        get => GetValue(IconColorProperty);
        set => SetValue(IconColorProperty, value);
    }

    public IconView()
    {
        // Hidden until a name resolves, so an un-iconed row shows no stray glyph.
        IsVisible = false;
    }

    // PackIconLucide derives from Avalonia's PathIcon and populates its Data geometry from Kind at runtime.
    // IconPacks' own packaged Lucide control theme targets an older Avalonia and throws on this version, so
    // we never include it; instead borrow PathIcon's style key to render Data through FluentTheme's PathIcon
    // theme (already included, version-matched). Without this the control resolves no theme and draws blank.
    protected override Type StyleKeyOverride => typeof(PathIcon);

    // The WCAG floor an icon's colour is held to against the surface. Icons are non-text graphics, so they
    // use the 3:1 graphics floor rather than text's higher ratio — enough to stay legible while keeping the
    // colour's hue recognizable.
    private const double IconContrastRatio = 3.0;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IconNameProperty)
            ApplyName();
        else if (change.Property == IconColorProperty)
            ApplyColor();
    }

    // Resolve the colour once we are in the tree: the surface brush a contrast pass needs only resolves then,
    // and re-entering the tree (e.g. after a theme change rebuilds the panel) re-picks the current surface.
    // Only the palette path overrides Foreground; an icon with no IconColor keeps its inherited/explicit ink.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (!string.IsNullOrWhiteSpace(IconColor))
            ApplyColor();
    }

    private void ApplyName()
    {
        if (!string.IsNullOrWhiteSpace(IconName)
            && Enum.TryParse<PackIconLucideKind>(IconName, ignoreCase: true, out var kind))
        {
            Kind = kind;
            IsVisible = true;
        }
        else
        {
            IsVisible = false;
        }
    }

    private void ApplyColor()
    {
        // No palette colour → inherit the surrounding (already contrast-aware) text/ink, exactly as before.
        if (string.IsNullOrWhiteSpace(IconColor) || !Palette.TryGetValue(IconColor, out var color))
        {
            Foreground = null;
            return;
        }

        // A palette colour picks the same darker/lighter contrast adjustment text does, against the theme's
        // surface — so a brand-coloured icon stays legible on any theme rather than vanishing into the panel.
        var surface = SurfaceColor();
        Foreground = new SolidColorBrush(
            surface is { } background ? Contrast.Ensure(color, background, IconContrastRatio) : color);
    }

    // The representative colour of the panel surface the icon sits on, or null before the icon is in the tree
    // (no resources to resolve yet). Matches the brush the theme engine contrasts text against.
    private Color? SurfaceColor()
    {
        if (!this.TryFindResource("ThemeBackgroundBrush", out var resource))
            return null;

        return resource switch
        {
            ISolidColorBrush solid => solid.Color,
            IGradientBrush { GradientStops.Count: > 0 } gradient =>
                gradient.GradientStops[gradient.GradientStops.Count / 2].Color,
            _ => null,
        };
    }
}
