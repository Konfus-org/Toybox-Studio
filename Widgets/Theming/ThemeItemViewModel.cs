using CommunityToolkit.Mvvm.ComponentModel;
using Toybox.Studio.Widgets.PropertyGrid;

namespace Toybox.Studio.Widgets.Theming;

/// <summary>
/// One row in the Themes list property: a theme's name plus whether it is the active selection. Ticking its
/// radio applies that theme through the supplied callback. The callback fires only on a genuine user
/// selection — the initial state is pushed in with the callback suppressed, so building the list never
/// re-applies a theme.
///
/// It is a <see cref="PropertyViewModel"/> so it renders with the grid's shared row chrome (label gutter +
/// depth elbow) and participates in the settings search like any other row — its name is matched, and the
/// parent <see cref="ThemePropertyViewModel"/> keeps it visible through <c>FilterChildren</c>.
/// </summary>
public sealed partial class ThemeItemViewModel : PropertyViewModel
{
    private readonly Action<string> _activate;
    private readonly bool _suppress;

    public ThemeItemViewModel(string name, bool isActive, int depth, Action<string> activate)
        : base(new PropertyNode { Name = name, Label = name, Type = "string" })
    {
        Depth = depth;
        _activate = activate;
        _suppress = true;
        IsActive = isActive;
        _suppress = false;
    }

    // A theme row has no engine "default" to revert to or "modified" state to flag, so it shows no right-hand
    // indicator dot. Reporting as composite resolves State to None (the indicator slot stays empty) while the
    // row still renders as a single leaf PropertyRow via the custom template.
    public override bool IsComposite => true;

    /// <summary>Whether this is the applied theme; selecting it applies it.</summary>
    [ObservableProperty]
    public partial bool IsActive { get; set; }

    partial void OnIsActiveChanged(bool value)
    {
        if (value && !_suppress)
            _activate(Name);
    }
}
