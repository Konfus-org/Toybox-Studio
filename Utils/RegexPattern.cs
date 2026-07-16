using Newtonsoft.Json;
using System.Text.RegularExpressions;

namespace Toybox.Studio.Utils;

/// <summary>
/// A regular-expression pattern as an editable value: the raw pattern text plus the ability to
/// validate it and test inputs against it. Its own type (rather than a bare string) so the property
/// grid gives it the validating regex editor — an invalid pattern still edits freely, it just flags
/// itself instead of throwing. An empty pattern is the "not configured" default (it matches nothing).
/// Immutable; assign a new one to change it. Persists as the bare pattern string (see
/// <see cref="RegexPatternConverter"/>), so a settings file stays hand-readable.
/// </summary>
[JsonConverter(typeof(RegexPatternConverter))]
public readonly record struct RegexPattern
{
    public RegexPattern(string? pattern) => Pattern = pattern ?? string.Empty;

    /// <summary>The raw pattern text; empty when unconfigured (never null through the constructor).</summary>
    public string Pattern { get; init; }

    /// <summary>Nothing to match on — the default, unconfigured state.</summary>
    public bool IsEmpty => string.IsNullOrEmpty(Pattern);

    /// <summary>Whether the pattern compiles as a .NET regex. An empty pattern is trivially valid.</summary>
    public bool IsValid => TryCompile(out _, out _);

    /// <summary>The compiler's complaint for an invalid pattern, or null when it compiles.</summary>
    public string? Error => TryCompile(out _, out var error) ? null : error;

    /// <summary>
    /// Whether <paramref name="input"/> matches. An empty (unconfigured) pattern matches nothing; an
    /// invalid pattern also matches nothing, since it can't be honored.
    /// </summary>
    public bool IsMatch(string input) =>
        !IsEmpty && TryCompile(out var regex, out _) && regex!.IsMatch(input);

    public override string ToString() => Pattern ?? string.Empty;

    private bool TryCompile(out Regex? regex, out string? error)
    {
        regex = null;
        error = null;
        if (string.IsNullOrEmpty(Pattern))
            return true;

        try
        {
            regex = new Regex(Pattern, RegexOptions.CultureInvariant);
            return true;
        }
        catch (ArgumentException exception)
        {
            error = exception.Message;
            return false;
        }
    }
}
