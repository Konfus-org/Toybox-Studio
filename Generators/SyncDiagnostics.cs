using Microsoft.CodeAnalysis;

namespace Toybox.Studio.Generators;

/// <summary>Every diagnostic the EngineSync generator can report, in one place.</summary>
internal static class SyncDiagnostics
{
    private const string Category = "Toybox.EngineSync";

    public static readonly DiagnosticDescriptor ClassNotPartial = new(
        "TBX001",
        "Engine-synced class must be partial",
        "'{0}' uses [EngineSync] but is not partial; the generator cannot add its implementation",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MemberNotPartial = new(
        "TBX002",
        "Engine-synced member must be partial",
        "'{0}' has [EngineSync] but is not a partial {1}; declare it partial so the generator can implement it",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor BaseConflict = new(
        "TBX003",
        "Engine-synced properties need the sync base",
        "'{0}' declares engine-synced properties but inherits '{1}', so the generator cannot inject the sync base; "
        + "derive from a synced type or move the properties to one",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MissingCommand = new(
        "TBX004",
        "Engine-synced member has no sync command",
        "'{0}' has no sync command: pass one to [EngineSync] or set a class-level [EngineSync] default",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NoCodec = new(
        "TBX005",
        "No wire codec for the synced type",
        "'{0}' has type '{1}', which has no built-in wire codec; pass an IWireConverter<T> via the attribute's converter",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor RelayOnSyncedObject = new(
        "TBX006",
        "Relay commands belong on view models",
        "'{0}' sets Relay = true inside an engine-synced object; relay commands are for view models — "
        + "expose the method and wrap it in an explicit view model instead",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MissingEngineMember = new(
        "TBX007",
        "No Engine member for the generated method",
        "'{0}' declares [EngineSync] methods but '{1}' has no accessible Engine field, property, or constructor "
        + "parameter for them to send through",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor BadExtra = new(
        "TBX008",
        "Unrecognized [EngineSync] payload entry",
        "The payload entry '{0}' on '{1}' is neither \"key=constant\" nor the name of an accessible member with a wire codec",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor BadMethodShape = new(
        "TBX009",
        "Engine-synced method has the wrong shape",
        "'{0}' must return Task<Result> or Task<Result<T>> and take only payload parameters plus an "
        + "optional CancellationToken",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MirrorAccessorMismatch = new(
        "TBX010",
        "Property accessors don't match the sync mode",
        "'{0}' must be declared {1} for its sync mode ({2})",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor BadAddressTemplate = new(
        "TBX012",
        "Invalid [EngineSync] address template",
        "The address template '{0}' on '{1}' is invalid: {2}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor RelayTooManyParameters = new(
        "TBX011",
        "Relay command supports at most one value parameter",
        "'{0}' sets Relay = true but takes more than one non-CancellationToken parameter; relay commands bind at most one",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor BadEventShape = new(
        "TBX013",
        "Engine-synced event has the wrong shape",
        "'{0}' must be a partial event whose delegate is System.Action<T> (one payload parameter)",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor QueryNeedsSyncBase = new(
        "TBX014",
        "Typed-reply methods belong on engine-synced objects",
        "'{0}' returns Task<Result<T>> but '{1}' is not an engine-synced object; typed-reply queries "
        + "need the sync base — move the method to a synced type",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
