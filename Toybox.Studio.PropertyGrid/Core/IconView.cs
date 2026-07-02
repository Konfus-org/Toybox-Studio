using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using IconPacks.Avalonia.Lucide;
using Toybox.Studio.Utils;

namespace Toybox.Studio.PropertyGrid;

/// <summary>
/// Renders the editor icon a type advertises through its [[tbx::icon("Name", Color::X)]] attribute.
/// <see cref="IconName"/> is a strongly-typed <see cref="Icon"/>; <see cref="Icon.None"/> (the
/// default) simply hides the glyph. <see cref="IconColor"/> is a direct Avalonia <see cref="Color"/> — code sets
/// it from the shared <see cref="Utils.Colors"/> palette consts (e.g. <c>Colors.Blue</c>), and in XAML it is an
/// <c>{x:Static Colors.X}</c> reference; null inherits the surrounding (already contrast-aware) ink.
/// </summary>
public sealed class IconView : PackIconLucide
{
    public static readonly StyledProperty<Icon> IconNameProperty =
        AvaloniaProperty.Register<IconView, Icon>(nameof(IconName));

    public static readonly StyledProperty<Color?> IconColorProperty =
        AvaloniaProperty.Register<IconView, Color?>(nameof(IconColor));

    // The WCAG floor an icon's colour is held to against the surface. Icons are non-text graphics, so they
    // use the 3:1 graphics floor rather than text's higher ratio — enough to stay legible while keeping the
    // colour's hue recognizable.
    private const double IconContrastRatio = 3.0;

    public IconView()
    {
        // Hidden until a name resolves, so an un-iconed row shows no stray glyph.
        IsVisible = false;
    }

    public Icon IconName
    {
        get => GetValue(IconNameProperty);
        set => SetValue(IconNameProperty, value);
    }

    public Color? IconColor
    {
        get => GetValue(IconColorProperty);
        set => SetValue(IconColorProperty, value);
    }

    // PackIconLucide derives from Avalonia's PathIcon and populates its Data geometry from Kind at runtime.
    // IconPacks' own packaged Lucide control theme targets an older Avalonia and throws on this version, so
    // we never include it; instead borrow PathIcon's style key to render Data through FluentTheme's PathIcon
    // theme (already included, version-matched). Without this the control resolves no theme and draws blank.
    protected override Type StyleKeyOverride => typeof(PathIcon);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IconNameProperty)
            ApplyName();
        else if (change.Property == IconColorProperty)
            ApplyColor();
    }

    // Re-sync the glyph every time it (re-)enters the tree. Visibility and the geometry are driven imperatively
    // off IconName, and the control starts hidden — so a glyph that's recycled or re-realized without a fresh
    // IconName change notification (e.g. when the dock rebuilds a tab strip / chrome header as panels stream on
    // engine start) would otherwise stay stuck hidden. Re-running ApplyName here makes the control self-heal:
    // its visibility and Kind always match its current IconName whenever it's shown. ApplyColor likewise re-picks
    // the current surface for the contrast pass (the brush it needs only resolves once we're in the tree).
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ApplyName();
        if (IconColor is not null)
            ApplyColor();
    }

    private void ApplyName()
    {
        if (IconName != Icon.None)
        {
            Kind = IconName;
            IsVisible = true;
        }
        else
        {
            IsVisible = false;
        }
    }

    private void ApplyColor()
    {
        // No colour → inherit the surrounding (already contrast-aware) text/ink, exactly as before.
        if (IconColor is not { } color)
        {
            Foreground = null;
            return;
        }

        // A palette colour picks the same darker/lighter contrast adjustment text does, against the theme's
        // surface — so a brand-coloured icon stays legible on any theme rather than vanishing into the panel.
        // ThemeBackgroundColor is the exact colour the theme engine contrasts text against (published by
        // ThemeApplier); resolving it directly keeps the icon and text in lock-step. It only resolves once
        // we're in the tree, so an icon attached before then keeps its raw palette colour until OnAttached.
        Foreground = new SolidColorBrush(
            this.TryFindResource("ThemeBackgroundColor", out var resource) && resource is Color background
                ? Contrast.Ensure(color, background, IconContrastRatio)
                : color);
    }
}
