using System.Text;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// Turns a raw identifier (a field key or component type name) into a human-readable label: underscores,
/// hyphens and spaces become word breaks, camelCase/PascalCase splits into words, and each word is
/// capitalized. Array element keys ("[0]") are left untouched. Shared by the property rows and the
/// component headers so a component reads the same way as the properties beneath it.
/// </summary>
public static class NameHumanizer
{
    public static string Humanize(string name)
    {
        if (string.IsNullOrEmpty(name) || name[0] == '[')
            return name;

        var builder = new StringBuilder(name.Length + 4);
        var startWord = true;
        var previous = '\0';
        foreach (var character in name)
        {
            if (character is '_' or '-' or ' ')
            {
                if (builder.Length > 0 && builder[^1] != ' ')
                    builder.Append(' ');
                startWord = true;
                previous = character;
                continue;
            }

            // Insert a break at a camelCase boundary (a capital following a lower-case letter or digit).
            if (builder.Length > 0 && char.IsUpper(character) && (char.IsLower(previous) || char.IsDigit(previous)))
            {
                builder.Append(' ');
                startWord = true;
            }

            builder.Append(startWord ? char.ToUpperInvariant(character) : character);
            startWord = false;
            previous = character;
        }

        return builder.ToString();
    }
}
