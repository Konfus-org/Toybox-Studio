using Toybox.Studio.Utils;

namespace Toybox.Studio.PropertyGrid.Slots;

/// <summary>
/// Edits a <see cref="RegexPattern"/> in a text box that validates as you type: the pattern is written
/// straight back on every edit (an invalid one commits too — it is still the value the user means), and
/// <see cref="IsValid"/>/<see cref="Error"/> drive the view's inline error state so a broken pattern is
/// visible rather than silently ignored. The built-in value slot for any <see cref="RegexPattern"/>
/// property.
/// </summary>
public sealed class RegexValueViewModel : ValueViewModel
{
    public RegexValueViewModel(PropertyValueAccessor accessor) : base(accessor)
    {
        // The base refreshes Value on any accessor change (a reset, another slot's write); the validity
        // read-outs must follow it, so a reset back to a valid pattern also clears the error state.
        accessor.Changed += RefreshValidity;
    }

    public string Value
    {
        get => Current.Pattern;
        set
        {
            Accessor.Set(new RegexPattern(value));
            RefreshValidity();
        }
    }

    /// <summary>Whether the current pattern compiles — false tints the field and surfaces
    /// <see cref="Error"/>.</summary>
    public bool IsValid => Current.IsValid;

    /// <summary>The compiler's complaint for the current pattern, or null when it is valid.</summary>
    public string? Error => Current.Error;

    private RegexPattern Current => Accessor.Get() as RegexPattern? ?? default;

    private void RefreshValidity()
    {
        OnPropertyChanged(nameof(IsValid));
        OnPropertyChanged(nameof(Error));
    }
}
