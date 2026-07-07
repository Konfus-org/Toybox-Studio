namespace Toybox.Studio.PropertyGrid;

/// <summary>
/// The one place the grid's numeric-type knowledge lives: which CLR types edit as numbers at all (the
/// reflection factory's leaf test) and which of those are integers (the number editor's step and
/// format). Supporting a new numeric shape means touching only these sets.
/// </summary>
internal static class NumericTypes
{
    private static readonly HashSet<Type> Integers =
    [
        typeof(sbyte), typeof(byte), typeof(short), typeof(ushort),
        typeof(int), typeof(uint), typeof(long), typeof(ulong),
    ];

    private static readonly HashSet<Type> Fractionals =
    [
        typeof(float), typeof(double), typeof(decimal),
    ];

    public static bool IsNumeric(Type type) => Integers.Contains(type) || Fractionals.Contains(type);

    public static bool IsInteger(Type type) => Integers.Contains(type);
}
