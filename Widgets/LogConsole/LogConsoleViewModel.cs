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
        var severity = entry switch
        {
            { IsError: true } => ConsoleSeverity.Error,
            { IsWarning: true } => ConsoleSeverity.Warning,
            { IsStudio: true } => ConsoleSeverity.Accent,
            _ => ConsoleSeverity.Normal,
        };

        Console.Append(new ConsoleLine(entry.Message, severity));
    }
}
