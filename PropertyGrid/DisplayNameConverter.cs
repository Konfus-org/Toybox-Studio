using Avalonia.Data.Converters;
using System.Globalization;
using System.Text;

namespace Toybox.Studio.PropertyGrid;

/// <summary>
/// Renders an identifier-ish row label as display text: dotted ids split into words
/// ("edit.openSettings" → "Edit Open Settings"), camelCase runs spaced, every word capitalized.
/// Already-friendly labels ("Hide Engine Window") and index labels ("[0]") pass through unchanged.
/// The node views bind their label text through <see cref="Instance"/>, so any grid row named by a
/// wire identifier reads as words.
/// </summary>
public sealed class DisplayNameConverter : IValueConverter
{
    public static DisplayNameConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string text ? ToDisplayName(text) : value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("Display names are one-way.");

    public static string ToDisplayName(string text)
    {
        var display = new StringBuilder(text.Length + 8);
        var startOfWord = true;
        var previous = '\0';
        foreach (var character in text)
        {
            if (character == '.')
            {
                display.Append(' ');
                startOfWord = true;
                previous = ' ';
                continue;
            }

            if (char.IsUpper(character) && (char.IsLower(previous) || char.IsDigit(previous)))
                display.Append(' ');

            display.Append(startOfWord ? char.ToUpperInvariant(character) : character);
            startOfWord = character == ' ';
            previous = character;
        }

        return display.ToString();
    }
}
