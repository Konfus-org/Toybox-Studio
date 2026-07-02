using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Toybox.Studio.Generators;

/// <summary>
/// Emits the engine-synced public property for every field marked <c>[EngineSync]</c> on a
/// <c>EngineSyncedObject</c> subclass — the editor-side analogue of <c>CommunityToolkit.Mvvm</c>'s
/// <c>[ObservableProperty]</c>. The generated setter raises <c>PropertyChanged</c> (via
/// <c>ObservableObject.SetProperty</c>) and then calls <c>Sync(...)</c> to push the change to the engine per
/// the field's <c>EngineSyncMode</c> (and, when set, an <c>IEngineSyncConverter</c> for custom value (de)serialization). It also
/// overrides <c>ApplyField</c> so engine-pushed values route back to the right setter (run under suppression, so
/// they update the UI without echoing). A field's wire name comes from <c>[EngineSync("wire")]</c> or, by default,
/// the field name with its leading underscore stripped and snake_cased.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class EngineSyncGenerator : IIncrementalGenerator
{
    private const string AttributeName = "Toybox.Studio.EngineApi.EngineSyncAttribute";

    // The whole EngineSync family (EngineSyncMode, EngineSyncValue) lives in Services/EngineApi; generated code
    // fully-qualifies each from that one namespace.
    private const string EngineApiNamespace = "global::Toybox.Studio.EngineApi";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var fields = context.SyntaxProvider.ForAttributeWithMetadataName(
            AttributeName,
            static (node, _) => true,
            static (ctx, _) => Describe(ctx))
            .Where(static field => field is not null)
            .Select(static (field, _) => field!);

        context.RegisterSourceOutput(fields.Collect(), static (spc, all) => Emit(spc, all));
    }

    private static SyncedField? Describe(GeneratorAttributeSyntaxContext ctx)
    {
        if (ctx.TargetSymbol is not IFieldSymbol field || field.ContainingType is not { } owner)
            return null;

        // Positional ctor: (string? wire, Type? converter, EngineSyncMode mode, int windowMs). Every call site is bare
        // [EngineSync] today, so omitted args fall back to the defaults below.
        var args = ctx.Attributes[0].ConstructorArguments;
        var wireOverride = args.Length >= 1 ? args[0].Value as string : null;
        var converter = args.Length >= 2 ? args[1].Value as INamedTypeSymbol : null;
        var mode = args.Length >= 3 && args[2].Value is int m ? m : 0;
        var windowMs = args.Length >= 4 && args[3].Value is int w ? w : 0;

        var property = ToPropertyName(field.Name);
        // A converter is the escape hatch: it overrides the default (de)serialization for a binary / odd-shaped
        // field. Otherwise the generic EngineSyncValue path maps the CLR type.
        var (write, read) = converter is not null ? ConverterCalls(converter) : MapType(field.Type);

        return new SyncedField(
            owner.ContainingNamespace.IsGlobalNamespace ? null : owner.ContainingNamespace.ToDisplayString(),
            owner.Name,
            owner.IsRecord ? "record" : "class",
            property,
            field.Name,
            field.Type.ToDisplayString(FullyQualified),
            string.IsNullOrEmpty(wireOverride) ? property.ToSnakeCase() : wireOverride!,
            mode switch { 1 => "Timer", 2 => "Manual", 3 => "Hydrate", 4 => "ReadOnly", 5 => "Stream", _ => "OnChanged" },
            windowMs,
            write,
            read,
            HidesBaseMember(owner, property));
    }

    // Whether an accessible same-named property/field exists on a base type, so the generated property must be
    // declared `new` (e.g. a Material's typed `Type` enum over the base `Asset.Type` string). Without it the
    // compiler raises CS0108 (an error under warnings-as-errors); emitting `new` only when something is actually
    // hidden avoids the converse CS0109.
    private static bool HidesBaseMember(INamedTypeSymbol owner, string property)
    {
        for (var baseType = owner.BaseType; baseType is not null; baseType = baseType.BaseType)
        {
            if (baseType.GetMembers(property).Any(member =>
                    member is IPropertySymbol or IFieldSymbol
                    && member.DeclaredAccessibility != Accessibility.Private))
                return true;
        }

        return false;
    }

    private static void Emit(SourceProductionContext spc, IEnumerable<SyncedField> all)
    {
        foreach (var group in all.GroupBy(field => (field.Namespace, field.ClassName, field.ClassKeyword)))
        {
            var (ns, className, keyword) = group.Key;
            var source = BuildClass(ns, className, keyword, group.ToList());
            var hint = (ns is null ? "" : ns.Replace('.', '_') + "_") + className + ".EngineSync.g.cs";
            spc.AddSource(hint, SourceText.From(source, Encoding.UTF8));
        }
    }

    private static string BuildClass(string? ns, string className, string keyword, List<SyncedField> fields)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        if (ns is not null)
        {
            sb.Append("namespace ").Append(ns).AppendLine();
            sb.AppendLine("{");
        }

        sb.Append("    partial ").Append(keyword).Append(' ').Append(className).AppendLine();
        sb.AppendLine("    {");

        foreach (var field in fields)
        {
            sb.Append("        public ").Append(field.HidesBase ? "new " : "").Append(field.TypeName)
                .Append(' ').Append(field.Property).AppendLine();
            sb.AppendLine("        {");
            sb.Append("            get => ").Append(field.FieldName).AppendLine(";");

            // ReadOnly fields are immutable identity: a get-only property, filled by ApplyField alone. Hydrate
            // fields notify on set but never push. The push behaviors set + Sync to the engine.
            if (field.Behavior != "ReadOnly")
            {
                sb.AppendLine("            set");
                sb.AppendLine("            {");
                if (field.Pushes)
                {
                    sb.Append("                if (SetProperty(ref ").Append(field.FieldName).AppendLine(", value))");
                    sb.Append("                    Sync(\"").Append(field.Wire).Append("\", ")
                        .Append(field.WriteCall).Append("(value), ").Append(EngineApiNamespace).Append(".EngineSyncMode.")
                        .Append(field.Behavior).Append(", ").Append(field.WindowMs).AppendLine(");");
                }
                else
                {
                    sb.Append("                SetProperty(ref ").Append(field.FieldName).AppendLine(", value);");
                }

                sb.AppendLine("            }");
            }

            sb.AppendLine("        }");
            sb.AppendLine();
        }

        sb.AppendLine("        protected override void ApplyField(string wire, global::Newtonsoft.Json.Linq.JToken value)");
        sb.AppendLine("        {");
        sb.AppendLine("            switch (wire)");
        sb.AppendLine("            {");
        foreach (var field in fields)
        {
            sb.Append("                case \"").Append(field.Wire).AppendLine("\":");
            if (field.Behavior == "ReadOnly")
                // No public setter — write the backing field directly and notify by property name.
                sb.Append("                    SetProperty(ref ").Append(field.FieldName).Append(", ")
                    .Append(field.ReadExpr).Append(", \"").Append(field.Property).AppendLine("\");");
            else
                sb.Append("                    ").Append(field.Property).Append(" = ").Append(field.ReadExpr).AppendLine(";");
            sb.AppendLine("                    break;");
        }

        sb.AppendLine("                default:");
        sb.AppendLine("                    base.ApplyField(wire, value);");
        sb.AppendLine("                    break;");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine();

        sb.AppendLine("        private static readonly string[] __reflectedWires = new string[]");
        sb.AppendLine("        {");
        foreach (var field in fields)
            sb.Append("            \"").Append(field.Wire).AppendLine("\",");
        sb.AppendLine("        };");
        sb.AppendLine();
        // SyncedWires must cover the WHOLE inheritance chain (a Trigger/Light base's fields plus the leaf's),
        // so consumers (load reconciliation, the inspector's reflection-vs-describe completeness gate) see every
        // wire the object actually has — not just the leaf class's own.
        sb.AppendLine("        public override global::System.Collections.Generic.IReadOnlyList<string> SyncedWires");
        sb.AppendLine("        {");
        sb.AppendLine("            get");
        sb.AppendLine("            {");
        sb.AppendLine("                var __all = new global::System.Collections.Generic.List<string>(base.SyncedWires);");
        sb.AppendLine("                __all.AddRange(__reflectedWires);");
        sb.AppendLine("                return __all;");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine();

        sb.AppendLine("        public override global::Newtonsoft.Json.Linq.JObject CollectSynced()");
        sb.AppendLine("        {");
        // Start from the base's collected values so an inherited (Trigger/Light base) field is included, then
        // overlay this class's own fields.
        sb.AppendLine("            var __collected = base.CollectSynced();");
        // Only push fields fold back into a buffered save; inbound-only state (Hydrate) and immutable
        // identity (ReadOnly) are engine-owned and must never be written back.
        foreach (var field in fields)
            if (field.Pushes)
                sb.Append("            __collected[\"").Append(field.Wire).Append("\"] = ")
                    .Append(field.WriteCall).Append('(').Append(field.Property).AppendLine(");");
        sb.AppendLine("            return __collected;");
        sb.AppendLine("        }");
        sb.AppendLine();

        sb.AppendLine("        public override string? WireFor(string property) => property switch");
        sb.AppendLine("        {");
        foreach (var field in fields)
            sb.Append("            \"").Append(field.Property).Append("\" => \"").Append(field.Wire).AppendLine("\",");
        sb.AppendLine("            _ => base.WireFor(property),");
        sb.AppendLine("        };");

        sb.AppendLine("    }");
        if (ns is not null)
            sb.AppendLine("}");

        return sb.ToString();
    }

    // The (write-method, read-expression) calls for a field with a custom IEngineSyncConverter — a fresh, stateless
    // converter instance per call. The caller appends the write argument (the setter passes `value`,
    // CollectSynced passes the property); the read expression reads the engine JToken named `value`.
    private static (string WriteCall, string Read) ConverterCalls(INamedTypeSymbol converter)
    {
        var conv = converter.ToDisplayString(FullyQualified);
        return ($"new {conv}().Write", $"new {conv}().Read(value)");
    }

    // Maps a field's CLR type to its (write-method, read-expression) EngineSyncValue calls. The write is just the
    // method — the caller appends the argument: the setter passes `value` (the new value), CollectSynced passes
    // the property. The read is a full expression reading the engine JToken named `value`.
    private static (string WriteCall, string Read) MapType(ITypeSymbol type)
    {
        string Call(string method) => $"{EngineApiNamespace}.EngineSyncValue.{method}";
        var full = type.ToDisplayString(FullyQualified);

        switch (type.SpecialType)
        {
            case SpecialType.System_Boolean:
                return (Call("Write"), $"{Call("ReadBool")}(value)");
            case SpecialType.System_Int32:
                return (Call("Write"), $"{Call("ReadInt")}(value)");
            case SpecialType.System_Single:
                return (Call("Write"), $"{Call("ReadSingle")}(value)");
            case SpecialType.System_String:
                return (Call("Write"), $"{Call("ReadString")}(value)");
        }

        if (type.TypeKind == TypeKind.Enum)
            return (Call("WriteEnum"), $"{Call("ReadEnum")}<{full}>(value)");

        return full switch
        {
            "global::System.Numerics.Vector2" => (Call("Write"), $"{Call("ReadVector2")}(value)"),
            "global::System.Numerics.Vector3" => (Call("Write"), $"{Call("ReadVector3")}(value)"),
            "global::System.Numerics.Vector4" => (Call("Write"), $"{Call("ReadVector4")}(value)"),
            "global::System.Numerics.Quaternion" => (Call("Write"), $"{Call("ReadQuaternion")}(value)"),
            "global::Avalonia.Media.Color" => (Call("Write"), $"{Call("ReadColor")}(value)"),
            "global::Toybox.Studio.Project.AssetHandle" =>
                (Call("WriteHandle"), $"{Call("ReadHandle")}(value)"),
            "global::System.Collections.Generic.List<global::Toybox.Studio.Project.AssetHandle>" =>
                (Call("WriteHandles"), $"{Call("ReadHandles")}(value)"),
            "global::System.Collections.Generic.IReadOnlyList<global::Toybox.Studio.Project.AssetHandle>" =>
                (Call("WriteHandles"), $"{Call("ReadHandles")}(value)"),
            // Nested value types (RenderTarget, MaterialConfig, Lod, …) and lists of them route through the
            // reflective bare codec, which keeps each nested member's exact wire shape (Newtonsoft's structural
            // form would not). WriteBare takes the value; ReadBare<T> reconstructs it (a List for a list field).
            _ => (Call("WriteBare"), $"{Call("ReadBare")}<{full}>(value)"),
        };
    }

    // A backing field name -> its public property name: strip a single leading underscore, capitalize the first.
    private static string ToPropertyName(string field)
    {
        var name = field.Length > 0 && field[0] == '_' ? field.Substring(1) : field;
        return name.Length == 0 ? field : char.ToUpperInvariant(name[0]) + name.Substring(1);
    }

    private static readonly SymbolDisplayFormat FullyQualified =
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    // The model the syntax pipeline carries per reflected field. A plain class (not a record) keeps the generator
    // on netstandard2.0 without an IsExternalInit polyfill.
    private sealed class SyncedField
    {
        public SyncedField(
            string? ns, string className, string classKeyword, string property, string fieldName, string typeName,
            string wire, string behavior, int windowMs, string writeCall, string readExpr, bool hidesBase)
        {
            Namespace = ns;
            ClassName = className;
            ClassKeyword = classKeyword;
            Property = property;
            FieldName = fieldName;
            TypeName = typeName;
            Wire = wire;
            Behavior = behavior;
            WindowMs = windowMs;
            WriteCall = writeCall;
            ReadExpr = readExpr;
            HidesBase = hidesBase;
        }

        public string? Namespace { get; }
        public string ClassName { get; }
        public string ClassKeyword { get; }
        public string Property { get; }
        public string FieldName { get; }
        public string TypeName { get; }
        public string Wire { get; }
        public string Behavior { get; }
        public int WindowMs { get; }

        /// <summary>Whether the field pushes its edits to the engine (the timed behaviors) versus being
        /// inbound-only read state (<c>Hydrate</c>/<c>ReadOnly</c>).</summary>
        public bool Pushes => Behavior is "OnChanged" or "Timer" or "Manual";
        public string WriteCall { get; }
        public string ReadExpr { get; }

        /// <summary>Whether the generated property hides a same-named member on a base type (e.g. a typed
        /// <c>Type</c> over <c>Asset.Type</c>), so the declaration needs the <c>new</c> modifier.</summary>
        public bool HidesBase { get; }
    }
}
