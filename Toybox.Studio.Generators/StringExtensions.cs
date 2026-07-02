using System.Text;

namespace Toybox.Studio.Generators;

/// <summary>
/// Generator-local string helpers. The generator targets netstandard2.0 and can't reference the editor's
/// Toybox.Studio.Core StringExtensions, so it keeps its own copy of the wire-name conversion (kept in sync
/// with Core's <c>ToSnakeCase</c>).
/// </summary>
internal static class StringExtensions
{
    /// <summary>Converts a PascalCase member name to the engine's snake_case wire name.</summary>
    public static string ToSnakeCase(this string name)
    {
        var sb = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c) && i > 0 && (char.IsLower(name[i - 1]) || char.IsDigit(name[i - 1])))
                sb.Append('_');
            sb.Append(char.ToLowerInvariant(c));
        }

        return sb.ToString();
    }
}
