using Microsoft.CodeAnalysis;

namespace Toybox.Studio.Generators;

/// <summary>A diagnostic captured during parsing, reported when the class's output is produced.</summary>
internal sealed class DiagnosticInfo(DiagnosticDescriptor descriptor, Location? location, params object?[] args)
{
    public Diagnostic ToDiagnostic() => Diagnostic.Create(descriptor, location ?? Location.None, args);
}
