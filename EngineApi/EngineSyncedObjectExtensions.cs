using System;
using System.Linq.Expressions;
using Toybox.Studio.Utils;

namespace Toybox.Studio.EngineApi;

/// <summary>
/// Expression-based, string-free targeting of a reflected object's fields: <c>obj.ResetAsync(o =&gt; o.Position)</c>
/// resolves the property to its engine wire name (via the generated <see cref="EngineSyncedObject.WireFor"/>) and
/// routes the op, so no property name is ever spelled at the call site. Only meaningful for a bound component — a
/// buffered asset isn't resident on the engine, so it has nothing to reset or compare there.
/// </summary>
public static class EngineSyncedObjectExtensions
{
    /// <summary>Resets the selected field to its engine default.</summary>
    public static Task<Result> ResetAsync<TObject, TProperty>(
        this TObject obj, Expression<Func<TObject, TProperty>> selector, CancellationToken ct = default)
        where TObject : EngineSyncedObject =>
        WireOf(obj, selector) is { } wire
            ? obj.ResetWireAsync(wire, ct)
            : Task.FromResult(Result.Fail("That property isn't a reflected field."));

    /// <summary>Whether the selected field currently holds its engine default.</summary>
    public static Task<Result<bool>> IsDefaultAsync<TObject, TProperty>(
        this TObject obj, Expression<Func<TObject, TProperty>> selector, CancellationToken ct = default)
        where TObject : EngineSyncedObject =>
        WireOf(obj, selector) is { } wire
            ? obj.IsDefaultWireAsync(wire, ct)
            : Task.FromResult(Result<bool>.Fail("That property isn't a reflected field."));

    // The wire name for a property-selector lambda (m => m.Prop), peeling the boxing conversion the compiler
    // inserts when a value-typed property is selected as object.
    private static string? WireOf<TObject, TProperty>(TObject obj, Expression<Func<TObject, TProperty>> selector)
        where TObject : EngineSyncedObject
    {
        var body = selector.Body is UnaryExpression { Operand: { } operand } ? operand : selector.Body;
        return body is MemberExpression member ? obj.WireFor(member.Member.Name) : null;
    }
}
