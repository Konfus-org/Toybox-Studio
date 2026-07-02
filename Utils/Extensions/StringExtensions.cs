using System.Text;

namespace Toybox.Studio.Utils.Extensions;

public static class StringExtensions
{

    /// <summary>PascalCase → snake_case: lowercase, inserting '_' before each uppercase that follows a lowercase
    /// or digit.</summary>
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
