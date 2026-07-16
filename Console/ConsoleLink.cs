namespace Toybox.Studio.Console;

/// <summary>
/// A clickable span within a <see cref="ConsoleLine"/>'s text: the character range
/// <c>[Start, Start + Length)</c> of <see cref="ConsoleLine.Text"/> is rendered as a link, and choosing it
/// invokes the console's link command with the whole link. The console is content-agnostic about what a target
/// means — the log console hands it a file path (and, when the link points at a specific place in that file, a
/// 1-based <see cref="Line"/>) to open — so <see cref="Target"/> stays an opaque string.
/// </summary>
/// <param name="Line">The 1-based line the target points at, or 0 when the link addresses no specific line.</param>
public sealed record ConsoleLink(int Start, int Length, string Target, int Line = 0);
