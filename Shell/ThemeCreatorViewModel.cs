using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toybox.Studio.Services;

namespace Toybox.Studio.Shell;

/// <summary>
/// Authors a new editor theme. Seeded from the active theme so the user can duplicate-and-tweak, then
/// saves a brand-new Theme.json (built-in defaults are never overwritten) via the theme manager, which
/// selects and applies it. Holds no <see cref="Avalonia.Controls.Window"/> reference; it raises
/// <see cref="CloseRequested"/> for the window to close itself.
/// </summary>
public sealed partial class ThemeCreatorViewModel : ObservableObject
{
    private readonly ThemeManager _theme;

    public ThemeCreatorViewModel(ThemeManager theme)
    {
        _theme = theme;

        var source = theme.Active;
        Name = "";
        SelectedVariant = source.Variant;
        CornerRadius = source.CornerRadius;
        FontFamily = source.Font.Family;
        FontSize = source.Font.Size;
        MonospaceFamily = source.Font.Monospace;
        PrimaryHex = source.Colors.Primary;
        SecondaryHex = source.Colors.Secondary;
        TertiaryHex = source.Colors.Tertiary;
        ErrorHex = source.Colors.Error;
        WarningHex = source.Colors.Warning;
        InfoHex = source.Colors.Info;
        SuccessHex = source.Colors.Success;
        BackgroundHex = source.Colors.Background;
        SurfaceHex = source.Colors.Surface;
        TextHex = source.Colors.Text;
    }

    /// <summary>
    /// Raised when the dialog should close (after a successful create, or on cancel).
    /// </summary>
    public event Action? CloseRequested;

    public IReadOnlyList<ThemeMode> Variants => _theme.Variants;

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial ThemeMode SelectedVariant { get; set; }

    [ObservableProperty]
    public partial double CornerRadius { get; set; }

    [ObservableProperty]
    public partial string FontFamily { get; set; }

    [ObservableProperty]
    public partial double FontSize { get; set; }

    [ObservableProperty]
    public partial string MonospaceFamily { get; set; }

    [ObservableProperty]
    public partial string PrimaryHex { get; set; }

    [ObservableProperty]
    public partial string SecondaryHex { get; set; }

    [ObservableProperty]
    public partial string TertiaryHex { get; set; }

    [ObservableProperty]
    public partial string ErrorHex { get; set; }

    [ObservableProperty]
    public partial string WarningHex { get; set; }

    [ObservableProperty]
    public partial string InfoHex { get; set; }

    [ObservableProperty]
    public partial string SuccessHex { get; set; }

    [ObservableProperty]
    public partial string BackgroundHex { get; set; }

    [ObservableProperty]
    public partial string SurfaceHex { get; set; }

    [ObservableProperty]
    public partial string TextHex { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; set; }

    public bool HasError => !string.IsNullOrEmpty(Error);

    [RelayCommand]
    private void Create()
    {
        var theme = new Theme
        {
            Name = Name,
            Variant = SelectedVariant,
            CornerRadius = CornerRadius,
            Font = new ThemeFont { Family = FontFamily, Size = FontSize, Monospace = MonospaceFamily },
            Colors = new ThemePalette
            {
                Primary = PrimaryHex,
                Secondary = SecondaryHex,
                Tertiary = TertiaryHex,
                Error = ErrorHex,
                Warning = WarningHex,
                Info = InfoHex,
                Success = SuccessHex,
                Background = BackgroundHex,
                Surface = SurfaceHex,
                Text = TextHex,
            },
        };

        if (_theme.TryCreateTheme(theme, out var error))
            CloseRequested?.Invoke();
        else
            Error = error;
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke();
}
