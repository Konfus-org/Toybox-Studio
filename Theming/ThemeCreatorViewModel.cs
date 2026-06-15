using System.ComponentModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toybox.Studio.Dialogs;
using Toybox.Studio.Theming;

namespace Toybox.Studio.Shell;

/// <summary>
/// Authors a new editor theme. Seeded from the active theme so the user can duplicate-and-tweak. Every
/// edit is applied to the running editor live (a non-persisted preview) so the user sees the theme as they
/// build it; on create the new Theme.json is written (built-in defaults are never overwritten) and the user
/// is asked whether to switch to it. Declining, cancelling, or closing reverts the live preview to whatever
/// theme was active when the dialog opened. Holds no <see cref="Avalonia.Controls.Window"/> reference; it
/// raises <see cref="CloseRequested"/> for the window to close itself and takes a <see cref="Confirm"/>
/// delegate so the switch prompt can nest under the dialog.
/// </summary>
public sealed partial class ThemeCreatorViewModel : ObservableObject
{
    private readonly ThemeManager _theme;

    // The theme that was active when the dialog opened; the live preview is reverted to it unless the user
    // chooses to switch to the newly created theme.
    private readonly Theme _original;

    // Gates the live preview until the seed values are all in (the [ObservableProperty] setters fire during
    // construction), and marks when the new theme has been committed as active so close doesn't revert it.
    private bool _initialized;
    private bool _committed;

    public ThemeCreatorViewModel(ThemeManager theme)
    {
        _theme = theme;
        _original = theme.Active;

        var source = theme.Active;
        Name = "";
        SelectedVariant = source.Variant;
        CornerRadius = source.CornerRadius;
        FontFamily = source.Font.Family;
        FontSize = source.Font.Size;
        MonospaceFamily = source.Font.Monospace;
        Primary = Parse(source.Colors.Primary);
        Secondary = Parse(source.Colors.Secondary);
        Tertiary = Parse(source.Colors.Tertiary);
        Error = Parse(source.Colors.Error);
        Warning = Parse(source.Colors.Warning);
        Info = Parse(source.Colors.Info);
        Success = Parse(source.Colors.Success);
        Background = Parse(source.Colors.Background);
        Surface = Parse(source.Colors.Surface);
        Text = Parse(source.Colors.Text);

        _initialized = true;
    }

    /// <summary>
    /// Shows the "switch to the new theme?" prompt and returns the choice. Set by the host window so the
    /// prompt nests under the dialog; falls back to a main-window-owned popup when unset.
    /// </summary>
    public Func<string, string, Task<bool>> Confirm { get; set; } =
        (title, message) => Popups.ConfirmAsync(title, message);

    /// <summary>Parses a stored palette hex string, falling back to opaque magenta if it won't parse.</summary>
    private static Color Parse(string hex) =>
        Color.TryParse(hex, out var color) ? color : Colors.Magenta;

    /// <summary>Formats a colour back to palette hex: <c>#RRGGBB</c>, or <c>#RRGGBBAA</c> when translucent.</summary>
    private static string ToHex(Color c) =>
        c.A == 255 ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : $"#{c.R:X2}{c.G:X2}{c.B:X2}{c.A:X2}";

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
    public partial Color Primary { get; set; }

    [ObservableProperty]
    public partial Color Secondary { get; set; }

    [ObservableProperty]
    public partial Color Tertiary { get; set; }

    [ObservableProperty]
    public partial Color Error { get; set; }

    [ObservableProperty]
    public partial Color Warning { get; set; }

    [ObservableProperty]
    public partial Color Info { get; set; }

    [ObservableProperty]
    public partial Color Success { get; set; }

    [ObservableProperty]
    public partial Color Background { get; set; }

    [ObservableProperty]
    public partial Color Surface { get; set; }

    [ObservableProperty]
    public partial Color Text { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; set; }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>Snapshots the current editor fields into a <see cref="Theme"/>.</summary>
    private Theme BuildTheme() => new()
    {
        Name = Name,
        Variant = SelectedVariant,
        CornerRadius = CornerRadius,
        Font = new ThemeFont { Family = FontFamily, Size = FontSize, Monospace = MonospaceFamily },
        Colors = new ThemePalette
        {
            Primary = ToHex(Primary),
            Secondary = ToHex(Secondary),
            Tertiary = ToHex(Tertiary),
            Error = ToHex(Error),
            Warning = ToHex(Warning),
            Info = ToHex(Info),
            Success = ToHex(Success),
            Background = ToHex(Background),
            Surface = ToHex(Surface),
            Text = ToHex(Text),
        },
    };

    /// <summary>Re-applies the in-progress theme to the running editor as a live, non-persisted preview.</summary>
    private void ApplyPreview()
    {
        if (_initialized)
            _theme.PreviewTheme(BuildTheme());
    }

    // Any visual field changing re-applies the live preview. Name/validation properties don't affect
    // appearance, so they're skipped to avoid pointless re-applies while typing the name.
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(Name) or nameof(ErrorMessage) or nameof(HasError))
            return;
        ApplyPreview();
    }

    /// <summary>
    /// Reverts the live preview to the theme that was active when the dialog opened, unless the user
    /// committed to the new one. Called when the window closes (covers Cancel and the window's close button).
    /// </summary>
    public void OnClosed()
    {
        if (!_committed)
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
