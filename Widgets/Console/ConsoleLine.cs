namespace Toybox.Studio.Widgets.Console;

/// <summary>
/// One line in the generic console: text plus a visual severity.
/// </summary>
public sealed record ConsoleLine(string Text, ConsoleSeverity Severity = ConsoleSeverity.Normal)
{
    public bool IsAccent => Severity == ConsoleSeverity.Accent;

    public bool IsWarning => Severity == ConsoleSeverity.Warning;

    public bool IsError => Severity == ConsoleSeverity.Error;
}
