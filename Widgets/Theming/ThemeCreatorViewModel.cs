using Toybox.Studio.Utils;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toybox.Studio.Services.Dialogs;
using Toybox.Studio.Services.Theming;
using Toybox.Studio.Widgets.Colors;

namespace Toybox.Studio.Widgets.Theming;

/// <summary>
/// Authors a new editor theme. Seeded from the active theme so the user can duplicate-and-tweak. Editing is
/// CHEAP because it does NOT touch the running editor on every keystroke — applying the whole theme (gradient
/// brushes, dock keys, bitmap recolours, …) per edit made the dialog laggy. Instead the user hits
/// <see cref="PreviewCommand"/> to apply a snapshot on demand, and Create writes the Theme.json and offers to
/// switch. Closing without committing reverts whatever was previewed. Holds no
/// <see cref="Avalonia.Controls.Window"/> reference; it raises <see cref="CloseRequested"/> for the window to
/// close itself and takes a <see cref="Confirm"/> delegate so the switch prompt can nest under the dialog.
///
/// Each palette entry is edited through a <see cref="ColorGradientViewModel"/>, so any colour can be a flat
/// colour or a gradient.
/// </summary>
public sealed partial class ThemeCreatorViewModel : ObservableObject
{
    private readonly ThemeManager _theme;

    // The theme that was active when the dialog opened; a previewed theme is reverted to it unless the user
    // commits (creates + switches).
    private readonly Theme _original;

    // True once the user has applied a live preview (so closing reverts), and once the new theme is committed
    // as active (so closing does NOT revert).
    private bool _previewed;
    private bool _committed;

    public ThemeCreatorViewModel(ThemeManager theme)
    {
        _theme = theme;
        _original = theme.Active;

        var source = theme.Active;
        Name = "";
        CornerRadius = source.CornerRadius;
        ShadowsEnabled = source.ShadowsEnabled;
        ShadowAngle = source.ShadowAngle;
        FontFamily = source.Font.Family;
        FontSize = source.Font.Size;
        MonospaceFamily = source.Font.Monospace;

        Primary = new ColorGradientViewModel(source.Colors.Primary);
        Secondary = new ColorGradientViewModel(source.Colors.Secondary);
        Tertiary = new ColorGradientViewModel(source.Colors.Tertiary);
        Error = new ColorGradientViewModel(source.Colors.Error);
        Warning = new ColorGradientViewModel(source.Colors.Warning);
        Info = new ColorGradientViewModel(source.Colors.Info);
        Success = new ColorGradientViewModel(source.Colors.Success);
        Background = new ColorGradientViewModel(source.Colors.Background);
        Surface = new ColorGradientViewModel(source.Colors.Surface);
        Text = new ColorGradientViewModel(source.Colors.Text);
    }

    /// <summary>
    /// Shows the "switch to the new theme?" prompt and returns the choice. Set by the host window so the
    /// prompt nests under the dialog; falls back to a main-window-owned popup when unset.
    /// </summary>
    public Func<string, string, Task<bool>> Confirm { get; set; } =
        (title, message) => Popups.ConfirmAsync(title, message);

    /// <summary>
    /// Raised when the dialog should close (after a successful create, or on cancel).
    /// </summary>
    public event Action? CloseRequested;

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial double CornerRadius { get; set; }

    [ObservableProperty]
    public partial bool ShadowsEnabled { get; set; }

    [ObservableProperty]
    public partial double ShadowAngle { get; set; }

    [ObservableProperty]
    public partial string FontFamily { get; set; }

    [ObservableProperty]
    public partial double FontSize { get; set; }

    [ObservableProperty]
    public partial string MonospaceFamily { get; set; }

    public ColorGradientViewModel Primary { get; }

    public ColorGradientViewModel Secondary { get; }

    public ColorGradientViewModel Tertiary { get; }

    public ColorGradientViewModel Error { get; }

    public ColorGradientViewModel Warning { get; }

    public ColorGradientViewModel Info { get; }

    public ColorGradientViewModel Success { get; }

    public ColorGradientViewModel Background { get; }

    public ColorGradientViewModel Surface { get; }

    public ColorGradientViewModel Text { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; set; }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>Snapshots the current editor fields into a <see cref="Theme"/>.</summary>
    private Theme BuildTheme() => new()
    {
        Name = Name,
        CornerRadius = CornerRadius,
        ShadowsEnabled = ShadowsEnabled,
        ShadowAngle = ShadowAngle,
        Font = new ThemeFont { Family = FontFamily, Size = FontSize, Monospace = MonospaceFamily },
        Colors = new ThemePalette
        {
            Primary = Primary.ToModel(),
            Secondary = Secondary.ToModel(),
            Tertiary = Tertiary.ToModel(),
            Error = Error.ToModel(),
            Warning = Warning.ToModel(),
            Info = Info.ToModel(),
            Success = Success.ToModel(),
            Background = Background.ToModel(),
            Surface = Surface.ToModel(),
            Text = Text.ToModel(),
        },
    };

    /// <summary>Applies the in-progress theme to the running editor once, on demand (the Preview button).</summary>
    [RelayCommand]
    private void Preview()
    {
        _previewed = true;
        _theme.PreviewTheme(BuildTheme());
    }

    /// <summary>
    /// Reverts a live preview to the theme that was active when the dialog opened, unless the user committed
    /// to the new one. Called when the window closes (covers Cancel and the window's close button). A no-op
    /// when nothing was previewed, so a fast cancel costs nothing.
    /// </summary>
    public void OnClosed()
    {
        if (_previewed && !_committed)
            _theme.Apply(_original);
    }

    [RelayCommand]
    private async Task CreateAsync()
    {
        var theme = BuildTheme();
        if (!_theme.TryCreateTheme(theme, out var error))
        {
            ErrorMessage = error;
            return;
        }

        var switchNow = await Confirm(
                "Theme created",
                $"'{theme.Name}' has been created.\n\nSwitch to it now?")
            .ContinueOnSameContext();
        if (switchNow)
        {
            _theme.SetActiveTheme(theme.Name);
            _committed = true;
        }

        CloseRequested?.Invoke();
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke();
}
