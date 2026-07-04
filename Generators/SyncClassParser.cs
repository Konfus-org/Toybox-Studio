using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Toybox.Studio.Generators;

/// <summary>
/// Turns one [EngineSync]-marked class declaration into the emitter's model, collecting diagnostics as
/// it goes: resolves each member's merged attribute config, the wire codec or converter calls, the
/// payload extras, and the class's base situation (inject <c>EngineObject</c>, already rooted, or a
/// foreign base whose methods send through an Engine member).
/// </summary>
internal static class SyncClassParser
{
    private const string CancellationTokenName = "System.Threading.CancellationToken";
    private const string ResultTaskName = "System.Threading.Tasks.Task<Toybox.Studio.Utils.Result>";

    private static readonly SymbolDisplayFormat FullyQualified =
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions
            | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    /// <summary>The cheap syntax test: a class with an EngineSync attribute on itself or any member.</summary>
    public static bool LooksSynced(SyntaxNode node)
    {
        if (node is not ClassDeclarationSyntax declaration)
            return false;

        if (HasEngineSyncName(declaration.AttributeLists))
            return true;

        foreach (var member in declaration.Members)
            if (member is PropertyDeclarationSyntax or MethodDeclarationSyntax
                && HasEngineSyncName(member.AttributeLists))
                return true;

        return false;
    }

    public static SyncedClass? Parse(GeneratorSyntaxContext context, CancellationToken ct)
    {
        var declaration = (ClassDeclarationSyntax)context.Node;
        if (context.SemanticModel.GetDeclaredSymbol(declaration, ct) is not INamedTypeSymbol symbol)
            return null;

        // A class split across files parses once, from its first declaration.
        if (!ReferenceEquals(symbol.DeclaringSyntaxReferences[0].GetSyntax(ct), declaration))
            return null;

        // Nested classes aren't supported (nothing engine-mirrored should be one).
        if (symbol.ContainingType is not null)
            return null;

        var hasMarkers = SyncAttributeConfig.On(symbol) is not null || SyncedMembers(symbol).Any();
        if (!hasMarkers)
            return null;

        var diagnostics = new List<DiagnosticInfo>();
        if (!declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            diagnostics.Add(new DiagnosticInfo(
                SyncDiagnostics.ClassNotPartial, declaration.Identifier.GetLocation(), symbol.Name));
            return Model(
                symbol, injectBase: false, isRooted: false, null, null, [], [], diagnostics, emit: false);
        }

        var baseType = symbol.BaseType;
        var baseIsObject = baseType is null || baseType.SpecialType == SpecialType.System_Object;
        var baseRooted = IsRooted(baseType);
        var injectBase = !baseRooted && baseIsObject;
        var isRooted = baseRooted || injectBase;

        var defaults = SyncAttributeConfig.ClassDefaults(symbol);
        var properties = new List<SyncedProperty>();
        var methods = new List<SyncedMethod>();

        foreach (var (member, config) in SyncedMembers(symbol))
        {
            var merged = config.MergedWith(defaults);
            switch (member)
            {
                case IPropertySymbol property:
                    if (!isRooted)
                    {
                        diagnostics.Add(new DiagnosticInfo(
                            SyncDiagnostics.BaseConflict, Location(property), symbol.Name,
                            baseType!.ToDisplayString()));
                        continue;
                    }

                    if (ParseProperty(symbol, property, merged, diagnostics) is { } parsedProperty)
                        properties.Add(parsedProperty);
                    break;

                case IMethodSymbol method:
                    if (ParseMethod(symbol, method, merged, isRooted, diagnostics) is { } parsedMethod)
                        methods.Add(parsedMethod);
                    break;
            }
        }

        string? engineMember = null;
        if (!isRooted && methods.Count > 0)
        {
            engineMember = FindEngineMember(symbol);
            if (engineMember is null)
            {
                diagnostics.Add(new DiagnosticInfo(
                    SyncDiagnostics.MissingEngineMember, declaration.Identifier.GetLocation(),
                    methods[0].Name, symbol.Name));
                methods.Clear();
            }
        }

        // The address template comes from the class's OWN attribute only — a family root declares its
        // address once; derived classes inherit the override, never re-emit it.
        string? addressExpression = null;
        if (SyncAttributeConfig.On(symbol) is { Address: { } template } ownConfig && isRooted)
            addressExpression = AddressExpression(symbol, template, ownConfig.Location, diagnostics);

        return Model(
            symbol, injectBase, isRooted, addressExpression, engineMember,
            properties, methods, diagnostics, emit: true);
    }

    private static SyncedProperty? ParseProperty(
        INamedTypeSymbol owner, IPropertySymbol property, SyncAttributeConfig config,
        List<DiagnosticInfo> diagnostics)
    {
        if (!IsPartialDefinition(property))
        {
            diagnostics.Add(new DiagnosticInfo(
                SyncDiagnostics.MemberNotPartial, Location(property), property.Name, "property"));
            return null;
        }

        if (config.Command is not { } command)
        {
            diagnostics.Add(new DiagnosticInfo(SyncDiagnostics.MissingCommand, Location(property), property.Name));
            return null;
        }

        // Mirror values never push, so an editable setter would lie; a private one is fine — it lets
        // the class assign its own engine-owned state locally. Every other mode needs a real setter.
        var mode = (SyncModeValue)(config.Mode ?? 0);
        var isMirror = mode == SyncModeValue.Mirror;
        var setter = property.SetMethod;
        var setterAllowed = isMirror
            ? setter is null || setter.DeclaredAccessibility == Accessibility.Private
            : setter is not null;
        if (!setterAllowed)
        {
            diagnostics.Add(new DiagnosticInfo(
                SyncDiagnostics.MirrorAccessorMismatch, Location(property), property.Name,
                isMirror ? "get-only or with a private setter" : "with get and set", mode.ToString()));
            return null;
        }

        var typeDisplay = property.Type.ToDisplayString(FullyQualified);
        string writeCall;
        string readCall;
        string? converterDisplay = null;
        if (config.Converter is { } converter)
        {
            converterDisplay = converter.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var field = property.Name + "SyncConverter";
            writeCall = $"{field}.Write(({typeDisplay})value!)";
            readCall = $"{field}.Read(token)";
        }
        else if (WireCodecs.Find(property.Type) is { } codec)
        {
            writeCall = isMirror
                ? "global::Newtonsoft.Json.Linq.JValue.CreateNull()"
                : codec.WriteInvocation($"({typeDisplay})value!");
            readCall = codec.ReadInvocation("token");
        }
        else
        {
            diagnostics.Add(new DiagnosticInfo(
                SyncDiagnostics.NoCodec, Location(property), property.Name, property.Type.ToDisplayString()));
            return null;
        }

        var extras = ParseExtras(owner, property, config, diagnostics);
        return new SyncedProperty(
            property.Name,
            AccessibilityText(property.DeclaredAccessibility),
            typeDisplay,
            property.Type.IsReferenceType,
            command,
            config.Key ?? CamelCase(property.Name),
            mode.ToString(),
            config.BatchFrequencyMs ?? 0,
            isMirror,
            setter is not null,
            setter is not null && setter.DeclaredAccessibility != property.DeclaredAccessibility
                ? AccessibilityText(setter.DeclaredAccessibility) + " "
                : string.Empty,
            converterDisplay,
            writeCall,
            readCall,
            extras);
    }

    private static SyncedMethod? ParseMethod(
        INamedTypeSymbol owner, IMethodSymbol method, SyncAttributeConfig config,
        bool isRooted, List<DiagnosticInfo> diagnostics)
    {
        if (!IsPartialDefinition(method))
        {
            diagnostics.Add(new DiagnosticInfo(
                SyncDiagnostics.MemberNotPartial, Location(method), method.Name, "method"));
            return null;
        }

        if (config.Command is not { } command)
        {
            diagnostics.Add(new DiagnosticInfo(SyncDiagnostics.MissingCommand, Location(method), method.Name));
            return null;
        }

        if (method.ReturnType.ToDisplayString() != ResultTaskName)
        {
            diagnostics.Add(new DiagnosticInfo(SyncDiagnostics.BadMethodShape, Location(method), method.Name));
            return null;
        }

        var parameters = new List<SyncedMethod.Parameter>();
        foreach (var parameter in method.Parameters)
        {
            var typeDisplay = parameter.Type.ToDisplayString(FullyQualified);
            if (parameter.Type.ToDisplayString() == CancellationTokenName)
            {
                parameters.Add(new SyncedMethod.Parameter(parameter.Name, typeDisplay, true, string.Empty));
                continue;
            }

            if (WireCodecs.Find(parameter.Type) is not { } codec)
            {
                diagnostics.Add(new DiagnosticInfo(
                    SyncDiagnostics.NoCodec, Location(method), parameter.Name, parameter.Type.ToDisplayString()));
                return null;
            }

            parameters.Add(new SyncedMethod.Parameter(
                parameter.Name, typeDisplay, false, codec.WriteInvocation(parameter.Name)));
        }

        var relay = config.Relay;
        if (relay && isRooted)
        {
            diagnostics.Add(new DiagnosticInfo(SyncDiagnostics.RelayOnSyncedObject, Location(method), method.Name));
            relay = false;
        }

        if (relay && parameters.Count(parameter => !parameter.IsCancellationToken) > 1)
        {
            diagnostics.Add(new DiagnosticInfo(
                SyncDiagnostics.RelayTooManyParameters, Location(method), method.Name));
            relay = false;
        }

        var extras = ParseExtras(owner, method, config, diagnostics);
        return new SyncedMethod(
            method.Name, AccessibilityText(method.DeclaredAccessibility), command, relay, parameters, extras);
    }

    // Resolves the attribute's engineParams: a "key", value pair becomes a typed literal, a lone
    // nameof(Member) becomes a codec call over that sibling member, and "key=text" a string constant.
    private static List<SyncedExtra> ParseExtras(
        INamedTypeSymbol owner, ISymbol member, SyncAttributeConfig config, List<DiagnosticInfo> diagnostics)
    {
        var extras = new List<SyncedExtra>();
        var args = config.EngineParams;
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i] is not string entry)
            {
                diagnostics.Add(new DiagnosticInfo(
                    SyncDiagnostics.BadExtra, Location(member), args[i]?.ToString() ?? "null", member.Name));
                continue;
            }

            // A non-string follower makes this a "key", value pair.
            if (i + 1 < args.Count && args[i + 1] is not (string or null))
            {
                if (TypedConstantExpression(args[i + 1]!) is { } typed)
                    extras.Add(new SyncedExtra(entry, typed));
                else
                    diagnostics.Add(new DiagnosticInfo(
                        SyncDiagnostics.BadExtra, Location(member), $"{entry}, {args[i + 1]}", member.Name));
                i++;
                continue;
            }

            var separator = entry.IndexOf('=');
            if (separator > 0)
            {
                extras.Add(new SyncedExtra(
                    entry.Substring(0, separator), StringConstantExpression(entry.Substring(separator + 1))));
                continue;
            }

            if (FindMember(owner, entry) is { } sibling && WireCodecs.Find(sibling) is { } codec)
            {
                extras.Add(new SyncedExtra(CamelCase(entry), codec.WriteInvocation(entry)));
                continue;
            }

            diagnostics.Add(new DiagnosticInfo(SyncDiagnostics.BadExtra, Location(member), entry, member.Name));
        }

        return extras;
    }

    // Turns an address template ("entity/{EntityId}/{Name}") into the interpolated-string expression
    // the Address override returns, validating every placeholder against the class's members.
    private static string? AddressExpression(
        INamedTypeSymbol owner, string template, Location? location, List<DiagnosticInfo> diagnostics)
    {
        var builder = new StringBuilder("$\"");
        for (var i = 0; i < template.Length; i++)
        {
            var letter = template[i];
            if (letter == '{')
            {
                var end = template.IndexOf('}', i + 1);
                var member = end > i + 1 ? template.Substring(i + 1, end - i - 1) : null;
                if (member is null)
                {
                    diagnostics.Add(new DiagnosticInfo(
                        SyncDiagnostics.BadAddressTemplate, location, template, owner.Name,
                        "an unclosed '{' placeholder"));
                    return null;
                }

                if (FindMember(owner, member) is null)
                {
                    diagnostics.Add(new DiagnosticInfo(
                        SyncDiagnostics.BadAddressTemplate, location, template, owner.Name,
                        $"'{member}' is not an accessible member"));
                    return null;
                }

                builder.Append('{').Append(member).Append('}');
                i = end;
            }
            else if (letter == '}')
            {
                diagnostics.Add(new DiagnosticInfo(
                    SyncDiagnostics.BadAddressTemplate, location, template, owner.Name,
                    "a '}' without a matching '{'"));
                return null;
            }
            else
            {
                if (letter is '"' or '\\')
                    builder.Append('\\');
                builder.Append(letter);
            }
        }

        return builder.Append('"').ToString();
    }

    // The C# literal for one boxed attribute constant; null when the type has no literal form.
    private static string? TypedConstantExpression(object value) => value switch
    {
        bool flag => flag ? "true" : "false",
        int number => number.ToString(CultureInfo.InvariantCulture),
        long number => number.ToString(CultureInfo.InvariantCulture) + "L",
        float number => number.ToString("R", CultureInfo.InvariantCulture) + "f",
        double number => number.ToString("R", CultureInfo.InvariantCulture) + "d",
        _ => null,
    };

    private static string StringConstantExpression(string constant) =>
        "\"" + constant.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    // The type of a property or field named `name`, on the type or any base; null when absent.
    private static ITypeSymbol? FindMember(INamedTypeSymbol owner, string name)
    {
        for (var type = (INamedTypeSymbol?)owner; type is not null; type = type.BaseType)
            foreach (var member in type.GetMembers(name))
                switch (member)
                {
                    case IPropertySymbol property:
                        return property.Type;
                    case IFieldSymbol field:
                        return field.Type;
                }

        return null;
    }

    // Walks the base chain: rooted when it reaches EngineObject, or a class that will get it injected
    // (sync markers and an object base).
    private static bool IsRooted(INamedTypeSymbol? type)
    {
        while (type is not null && type.SpecialType != SpecialType.System_Object)
        {
            if (type.ToDisplayString() == EngineSyncGenerator.EngineObjectName)
                return true;

            var baseType = type.BaseType;
            var baseIsObject = baseType is null || baseType.SpecialType == SpecialType.System_Object;
            if (baseIsObject)
                return SyncAttributeConfig.On(type) is not null || SyncedMembers(type).Any();

            type = baseType;
        }

        return false;
    }

    // An Engine-typed member for a foreign-based class's generated methods: a field or property first,
    // then a constructor (incl. primary constructor) parameter, whose captures are in scope class-wide.
    private static string? FindEngineMember(INamedTypeSymbol type)
    {
        for (var current = (INamedTypeSymbol?)type; current is not null; current = current.BaseType)
            foreach (var member in current.GetMembers())
                switch (member)
                {
                    case IPropertySymbol { IsStatic: false } property when IsEngine(property.Type):
                        return property.Name;
                    case IFieldSymbol { IsStatic: false, IsImplicitlyDeclared: false } field when IsEngine(field.Type):
                        return field.Name;
                }

        foreach (var constructor in type.Constructors)
            foreach (var parameter in constructor.Parameters)
                if (IsEngine(parameter.Type))
                    return parameter.Name;

        return null;

        static bool IsEngine(ITypeSymbol type) => type.ToDisplayString() == EngineSyncGenerator.EngineName;
    }

    private static IEnumerable<(ISymbol Member, SyncAttributeConfig Config)> SyncedMembers(INamedTypeSymbol type)
    {
        foreach (var member in type.GetMembers())
        {
            if (member is not (IPropertySymbol or IMethodSymbol { MethodKind: MethodKind.Ordinary }))
                continue;

            if (SyncAttributeConfig.On(member) is { } config)
                yield return (member, config);
        }
    }

    private static SyncedClass Model(
        INamedTypeSymbol symbol, bool injectBase, bool isRooted, string? addressExpression,
        string? engineMember, List<SyncedProperty> properties, List<SyncedMethod> methods,
        List<DiagnosticInfo> diagnostics, bool emit)
    {
        var ns = symbol.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : symbol.ContainingNamespace.ToDisplayString();
        var typeParameters = symbol.TypeParameters.Length == 0
            ? string.Empty
            : "<" + string.Join(", ", symbol.TypeParameters.Select(parameter => parameter.Name)) + ">";
        return new SyncedClass(
            ns, symbol.Name, typeParameters, injectBase, isRooted, addressExpression, engineMember,
            properties, methods, diagnostics, emit);
    }

    private static bool IsPartialDefinition(ISymbol member) =>
        member.DeclaringSyntaxReferences.Select(reference => reference.GetSyntax()).Any(
            syntax => syntax is PropertyDeclarationSyntax property
                ? property.Modifiers.Any(SyntaxKind.PartialKeyword)
                : syntax is MethodDeclarationSyntax method
                    && method.Modifiers.Any(SyntaxKind.PartialKeyword)
                    && method.Body is null && method.ExpressionBody is null);

    private static bool HasEngineSyncName(SyntaxList<AttributeListSyntax> lists)
    {
        foreach (var list in lists)
            foreach (var attribute in list.Attributes)
            {
                var name = attribute.Name is QualifiedNameSyntax qualified
                    ? qualified.Right.Identifier.Text
                    : (attribute.Name as SimpleNameSyntax)?.Identifier.Text;
                if (name is "EngineSync" or "EngineSyncAttribute")
                    return true;
            }

        return false;
    }

    private static Location? Location(ISymbol symbol) =>
        symbol.Locations.FirstOrDefault(location => location.IsInSource);

    private static string AccessibilityText(Accessibility accessibility) => accessibility switch
    {
        Accessibility.Public => "public",
        Accessibility.Internal => "internal",
        Accessibility.Protected => "protected",
        Accessibility.ProtectedOrInternal => "protected internal",
        Accessibility.ProtectedAndInternal => "private protected",
        _ => "private",
    };

    private static string CamelCase(string name) => char.ToLowerInvariant(name[0]) + name.Substring(1);

    // Mirrors Toybox.Studio.EngineApi.SyncMode's member order.
    private enum SyncModeValue
    {
        Live,
        Batched,
        Manual,
        Mirror,
    }
}
