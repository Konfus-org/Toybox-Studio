using Microsoft.CodeAnalysis;

namespace Toybox.Studio.Generators;

/// <summary>
/// Maps a synced member's type to the <c>WireValue</c> codec calls the generated code uses. Mirrors the
/// overload set in <c>EngineApi/WireValue.cs</c> — extend both together. A type with no entry here needs
/// an explicit <c>IWireConverter&lt;T&gt;</c> on the attribute.
/// </summary>
internal static class WireCodecs
{
    private const string WireValue = "global::Toybox.Studio.EngineApi.WireValue";

    /// <summary>The codec for <paramref name="type"/>, or null when it has none built in.</summary>
    public static WireCodec? Find(ITypeSymbol type)
    {
        if (type.TypeKind == TypeKind.Enum)
        {
            var display = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return new WireCodec($"{WireValue}.WriteEnum", $"{WireValue}.ReadEnum<{display}>");
        }

        return type.ToDisplayString() switch
        {
            "bool" => Simple("ReadBool"),
            "int" => Simple("ReadInt"),
            "long" => Simple("ReadInt64"),
            "ulong" => Simple("ReadUInt64"),
            "float" => Simple("ReadSingle"),
            "double" => Simple("ReadDouble"),
            "string" or "string?" => Simple("ReadString"),
            "System.Numerics.Vector2" => Simple("ReadVector2"),
            "System.Numerics.Vector3" => Simple("ReadVector3"),
            "System.Numerics.Vector4" => Simple("ReadVector4"),
            "System.Numerics.Quaternion" => Simple("ReadQuaternion"),
            "Avalonia.Media.Color" => Simple("ReadColor"),
            _ => null,
        };
    }

    private static WireCodec Simple(string readMethod) =>
        new($"{WireValue}.Write", $"{WireValue}.{readMethod}");
}

/// <summary>One codec's write and read call targets; invocations are built over caller expressions.</summary>
internal sealed class WireCodec(string writeTarget, string readTarget)
{
    /// <summary>E.g. <c>WireValue.Write((global::System.Numerics.Vector3)value!)</c>.</summary>
    public string WriteInvocation(string valueExpression) => $"{writeTarget}({valueExpression})";

    /// <summary>E.g. <c>WireValue.ReadVector3(token)</c>.</summary>
    public string ReadInvocation(string tokenExpression) => $"{readTarget}({tokenExpression})";
}
