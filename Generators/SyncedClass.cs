using System.Collections.Generic;

namespace Toybox.Studio.Generators;

/// <summary>The parsed model of one [EngineSync]-marked class, ready to emit.</summary>
internal sealed class SyncedClass(
    string ns,
    string name,
    string typeParameters,
    bool injectBase,
    bool isRooted,
    string? addressExpression,
    string? engineMemberName,
    List<SyncedProperty> properties,
    List<SyncedMethod> methods,
    List<DiagnosticInfo> diagnostics,
    bool emit)
{
    /// <summary>The containing namespace; empty for the global namespace.</summary>
    public string Namespace { get; } = ns;

    public string Name { get; } = name;

    /// <summary>The type parameter list text (<c>"&lt;T&gt;"</c>), empty for non-generic classes.</summary>
    public string TypeParameters { get; } = typeParameters;

    /// <summary>Whether the generated partial declares <c>EngineObject</c> as the base type.</summary>
    public bool InjectBase { get; } = injectBase;

    /// <summary>Rooted classes send through the EngineObject plumbing; foreign-based classes (view
    /// models) send through <see cref="EngineMemberName"/>.</summary>
    public bool IsRooted { get; } = isRooted;

    /// <summary>The emitted <c>Address</c> override's expression (an interpolated string built from the
    /// class attribute's template), or null when the class declares no template.</summary>
    public string? AddressExpression { get; } = addressExpression;

    /// <summary>The Engine-typed member generated methods on a foreign-based class call through.</summary>
    public string? EngineMemberName { get; } = engineMemberName;

    public List<SyncedProperty> Properties { get; } = properties;

    public List<SyncedMethod> Methods { get; } = methods;

    public List<DiagnosticInfo> Diagnostics { get; } = diagnostics;

    /// <summary>False when a class-level error makes any generated output meaningless.</summary>
    public bool Emit { get; } = emit;

    public string HintName =>
        (Namespace.Length > 0 ? Namespace + "." : string.Empty) + Name + ".EngineSync.g.cs";
}
