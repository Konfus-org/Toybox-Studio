using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Toybox.Studio.Services.Theming;

namespace Toybox.Studio.Widgets.Colors;

/// <summary>
/// Editable view-model for a single theme colour expressed as a <see cref="ColorGradient"/>. Drives the
/// <see cref="ColorGradientView"/>: two colour stops, an angle, and a "solid" toggle that collapses the
/// gradient to a flat colour. A live <see cref="Preview"/> brush updates as the fields change, and hosts
/// (e.g. the Theme Creator) subscribe to <see cref="ObservableObject.PropertyChanged"/> to re-apply.
/// </summary>
public sealed partial class ColorGradientViewModel : ObservableObject
{
    public ColorGradientViewModel()
        : this(ColorGradient.Solid(Avalonia.Media.Colors.Black))
    {
    }

    // Preserved as-is (not user-editable in the creator yet) so a radial palette colour round-trips.
    private readonly ColorGradientKind _kind;

    /// <param name="solidOnly">
    /// When true the editor is locked to a single flat colour: the Solid/Gradient toggle, end stop and angle
    /// are hidden and the value can never become a gradient. Used where the backing value is a flat colour
    /// (e.g. an engine <c>{r,g,b,a}</c> in the property grid), which can't persist a gradient.
    /// </param>
    public ColorGradientViewModel(ColorGradient gradient, bool solidOnly = false)
    {
        Start = gradient.Start;
        End = gradient.End;
        Angle = gradient.Angle;
        IsSolid = gradient.IsSolid || solidOnly;
        SolidOnly = solidOnly;
        _kind = gradient.Kind;
    }

    /// <summary>True for a flat-colour-only editor (no gradient authoring); see the constructor.</summary>
    public bool SolidOnly { get; }

    /// <summary>The Solid/Gradient toggle is hidden when the editor is locked to a flat colour.</summary>
    public bool ShowSolidToggle => !SolidOnly;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Preview))]
    public partial Color Start { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Preview))]
    public partial Color End { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Preview))]
    public partial double Angle { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Preview))]
    [NotifyPropertyChangedFor(nameof(ShowGradientControls))]
    public partial bool IsSolid { get; set; }

    /// <summary>The end stop and angle only matter for a real gradient; hidden while solid or flat-only.</summary>
    public bool ShowGradientControls => !IsSolid && !SolidOnly;

    /// <summary>Live brush for the preview swatch — collapses to the start colour while solid.</summary>
    public IBrush Preview => ColorGradient.BuildBrush(Start, IsSolid ? Start : End, Angle, _kind);

    /// <summary>Snapshots the current edits back into a persistable <see cref="ColorGradient"/>.</summary>
    public ColorGradient ToModel() => IsSolid
        ? ColorGradient.Solid(Start)
        : new ColorGradient(Start, End, Angle, _kind);
}
