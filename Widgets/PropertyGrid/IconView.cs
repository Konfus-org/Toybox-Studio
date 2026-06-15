using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using IconPacks.Avalonia.Lucide;

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

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IconNameProperty)
            ApplyName();
        else if (change.Property == IconColorProperty)
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
        Foreground = !string.IsNullOrWhiteSpace(IconColor) && Palette.TryGetValue(IconColor, out var color)
            ? new SolidColorBrush(color)
            : null;
    }
}
