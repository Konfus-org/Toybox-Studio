using CommunityToolkit.Mvvm.ComponentModel;

namespace Toybox.Studio.Toolbar;

/// <summary>
/// An editable numeric chip a toolbar surfaces after its tools (the transform toolbar's snap amount):
/// the same drag-to-scrub / type spinner the property grid's number fields use, bridged to a domain
/// value through the supplied get/set. The spinner stays a plain number so both typing and scrubbing
/// behave exactly as the property grid's — the unit reads as separate <see cref="Prefix"/>/<see
/// cref="Suffix"/> labels (a leading × scaling, a trailing ° rotating) rather than baked into the text.
/// The owning <see cref="ToolbarViewModel"/> sets the presentation and calls <see cref="RefreshValue"/>
/// from its domain's changed event; all of it can move together (the transform toolbar's step, unit and
/// value all change with the active mode).
/// </summary>
public sealed partial class ToolbarNumberField : ObservableObject
{
    private readonly Func<double> _get;
    private readonly Action<double> _set;

    public ToolbarNumberField(Func<double> get, Action<double> set)
    {
        _get = get;
        _set = set;
    }

    /// <summary>The value the spinner edits (decimal for the <c>NumericUpDown</c>; the setter maps back
    /// to the domain's double). A null spinner value — an emptied text box — leaves the value unchanged.</summary>
    public decimal? Value
    {
        get => (decimal)_get();
        set
        {
            if (value is { } number)
                _set((double)number);
        }
    }

    /// <summary>The scrub/spinner step.</summary>
    [ObservableProperty]
    public partial decimal Increment { get; set; } = 0.1m;

    /// <summary>A unit shown before the number (the scale factor's ×); empty for none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPrefix))]
    public partial string Prefix { get; set; } = string.Empty;

    /// <summary>A unit shown after the number (the rotate step's °); empty for none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSuffix))]
    public partial string Suffix { get; set; } = string.Empty;

    /// <summary>Whether the field is shown right now (the transform toolbar shows it only while snapping
    /// is effectively on).</summary>
    [ObservableProperty]
    public partial bool IsVisible { get; set; }

    public bool HasPrefix => Prefix.Length > 0;

    public bool HasSuffix => Suffix.Length > 0;

    /// <summary>Re-reads the value from the domain (the owning toolbar calls this after a change that
    /// moved it — a mode switch, or the value being edited elsewhere).</summary>
    public void RefreshValue() => OnPropertyChanged(nameof(Value));
}
