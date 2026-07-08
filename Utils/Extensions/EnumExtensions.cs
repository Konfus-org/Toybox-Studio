using System.Reflection;
using System.Text;
using Toybox.Studio.Utils.Attributes;

namespace Toybox.Studio.Utils.Extensions;

/// <summary>
/// Extension helpers for enums.
/// </summary>
public static class EnumExtensions
{
    /// <summary>
    /// The display label for an enum member: its <see cref="DisplayNameAttribute"/> when present (for
    /// labels a member name can't spell, like "Don't Save"), else the member name with spaces re-inserted
    /// between its words ("DontSave" → "Dont Save").
    /// </summary>
    public static string GetDisplayName<TEnum>(this TEnum value) where TEnum : struct, Enum
    {
        var name = value.ToString();
        if (typeof(TEnum).GetField(name)?.GetCustomAttribute<DisplayNameAttribute>() is { } displayName)
            return displayName.Name;
        return Humanize(name);
    }

    private static string Humanize(string name)
    {
        var text = new StringBuilder(name.Length + 4);
        for (var index = 0; index < name.Length; index++)
        {
            if (index > 0 && char.IsUpper(name[index]) && !char.IsUpper(name[index - 1]))
                text.Append(' ');
            text.Append(name[index]);
        }

        return text.ToString();
    }
}
