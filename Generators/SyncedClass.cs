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
    List<SyncedEvent> events,
    List<SyncedMethod> methods,
    List<DiagnosticInfo> diagnostics,
    bool pathAddressed,
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

    public List<SyncedEvent> Events { get; } = events;

    public List<SyncedMethod> Methods { get; } = methods;

    public List<DiagnosticInfo> Diagnostics { get; } = diagnostics;

    /// <summary>Whether the class's pushes are world-qualified sync.set paths: the emitted
    /// <c>PathAddressed</c> override folds each property's key into the address (…/{key}) on
    /// set/reset/isDefault, with no separate key field. Only the base-injecting root emits it; derived
    /// types inherit the override.</summary>
    public bool PathAddressed { get; } = pathAddressed;

    /// <summary>False when a class-level error makes any generated output meaningless.</summary>
    public bool Emit { get; } = emit;

    public string HintName =>
        (Namespace.Length > 0 ? Namespace + "." : string.Empty) + Name + ".EngineSync.g.cs";
}
