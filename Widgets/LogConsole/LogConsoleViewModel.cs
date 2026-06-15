using Toybox.Studio.Widgets.Console;
using Toybox.Studio.Logging;

namespace Toybox.Studio.Widgets.LogConsole;

/// <summary>
/// The log-specific console: listens to <see cref="Logger"/> and pushes each entry into a
/// generic <see cref="ConsoleViewModel"/>, mapping log level/source to a console severity.
/// </summary>
public sealed class LogConsoleViewModel
{
    public LogConsoleViewModel(Logger logging)
    {
        logging.Logged += OnLogged;
    }

    public ConsoleViewModel Console { get; } = new();

    private void OnLogged(LogEntry entry)
    {
        // Color strictly by level so the console matches the engine's configured log colors
        // (engine.setLogColors pushes the same Info/Warning/Error palette) regardless of which source
        // emitted the line. The category is part of the message text ("[Category]…"), not a column.
        var severity = entry switch
        {
            { IsError: true } => ConsoleSeverity.Error,
            { IsWarning: true } => ConsoleSeverity.Warning,
            _ => ConsoleSeverity.Accent,
        };

        Console.Append(new ConsoleLine(entry.Message, severity));
    }
}
