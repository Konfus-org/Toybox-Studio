using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Text.RegularExpressions;
using Toybox.Studio.Coding;
using Toybox.Studio.Console;
using Toybox.Studio.Logging;
using Toybox.Studio.Utils.Composition;

namespace Toybox.Studio.LogConsole;

/// <summary>
/// The log console dockable's view-model: the one place the unified <see cref="Logger"/> stream meets the UI.
/// It subscribes to every logged line, maps its level to a console severity, and turns the line's
/// <c>[file:line]</c> source prefix into a clickable link back to the full source path the entry carries
/// (editor lines carry their caller path; engine lines carry theirs over the wire). It owns a generic
/// <see cref="ConsoleViewModel"/> (the searchable/copyable scrollback the view renders) and does no rendering
/// itself.
/// </summary>
public sealed partial class LogConsoleViewModel : ObservableObject, IDisposable
{
    // The first "[name.ext:line]" prefix in a message: locates the span to underline as the link. The
    // file-name class excludes brackets and colons so it can't span across "][", and the required "." then
    // ":digits" keeps it off category tags like "[Studio]" or "[Physics]".
    private static readonly Regex SourceToken =
        new(@"\[(?<file>[^\[\]:]+\.[A-Za-z0-9]+):(?<line>\d+)\]", RegexOptions.Compiled);

    private readonly Logger _log;
    private readonly CoderLauncher _coder;

    public LogConsoleViewModel(Logger log, CoderLauncher coder, ViewModelFactory viewModels)
    {
        _log = log;
        _coder = coder;
        Console = viewModels.Create<ConsoleViewModel>();

        // A fresh console per open replays the whole session: the backlog lives in the logger, not in this
        // (short-lived) view-model. Subscribe hands back everything logged so far and starts the live feed
        // in one atomic step, so nothing is missed or doubled across the seam.
        foreach (var entry in _log.Subscribe(OnLogged))
            OnLogged(entry);
    }

    /// <summary>The generic scrollback the view binds to (search, filters, copy, clear, tailing all live here).</summary>
    public ConsoleViewModel Console { get; }

    public void Dispose() => _log.Logged -= OnLogged;

    // Runs on the logging thread (UI for editor lines, an RPC thread for engine lines); Console.Append is
    // thread-safe and batches onto the UI thread, so this stays a cheap map-and-hand-off.
    private void OnLogged(LogEntry entry)
    {
        var severity = entry switch
        {
            { IsError: true } => ConsoleSeverity.Error,
            { IsWarning: true } => ConsoleSeverity.Warning,
            _ => ConsoleSeverity.Accent,
        };

        Console.Append(new ConsoleLine(entry.Message, severity, BuildLink(entry)));
    }

    // Links the message's "[file:line]" prefix (without its brackets) to the source path the entry carries,
    // or returns null — leaving it plain text — when the line has no source location (external/category-only
    // lines) or no "[file:line]" prefix to underline.
    private static ConsoleLink? BuildLink(LogEntry entry)
    {
        if (entry.Source is not { } source)
            return null;

        var match = SourceToken.Match(entry.Message);
        if (!match.Success)
            return null;

        // Link the text inside the brackets: skip the leading '[', drop the trailing ']'. Carry the source's
        // line so a click opens the file at exactly that line.
        return new ConsoleLink(match.Index + 1, match.Length - 2, source.File, source.Line);
    }

    // Every link this console builds is a source file (the regex only matches "[name.ext:line]" and the
    // resolver only maps the C/C++/C# source extensions), so open it in the in-app Coder rather than handing
    // it to the OS shell — the latter popped the Windows "Open With…" picker for engine .cpp/.h files, which
    // have no default association.
    [RelayCommand]
    private void OpenLink(ConsoleLink? link)
    {
        if (link is null || string.IsNullOrEmpty(link.Target))
            return;

        _coder.OpenScript(link.Target, link.Line);
    }
}
