namespace Toybox.Studio.Console;

/// <summary>
/// One line in the generic console: text, a visual severity, and an optional <see cref="ConsoleLink"/>
/// marking a clickable span within the text (e.g. a source <c>file:line</c> the log console links to its file).
/// </summary>
public sealed record ConsoleLine(string Text, ConsoleSeverity Severity = ConsoleSeverity.Normal, ConsoleLink? Link = null)
{
    public bool IsAccent => Severity == ConsoleSeverity.Accent;

    public bool IsWarning => Severity == ConsoleSeverity.Warning;

    public bool IsError => Severity == ConsoleSeverity.Error;
}
