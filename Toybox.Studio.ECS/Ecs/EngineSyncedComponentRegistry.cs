using System;
using System.Collections.Generic;
using System.Linq;
using Toybox.Studio.Ecs.Components;
using Toybox.Studio.Utils.Extensions;

namespace Toybox.Studio.Ecs;

/// <summary>
/// The map between the engine's component wire names and the typed <see cref="Component"/> subclasses that model
/// them, discovered once by reflection over this assembly using the same name convention the EngineSync codegen
/// relies on (the snake_case of the class name, via <c>ToSnakeCase()</c>). It is the C# side of the
/// engine sync catalog: the parser uses it to mint the right typed component per wire (falling back to
/// <see cref="UnknownComponent"/>), and the component catalog uses it to catch typed-vs-engine drift at connect.
/// A single editor instance, injected by the composition root.
/// </summary>
public sealed class EngineSyncedComponentRegistry
{
    private readonly IReadOnlyDictionary<string, Type> _byWire;

    public EngineSyncedComponentRegistry()
    {
        // Component lives in this assembly, so its assembly is the one carrying every typed component. The
        // untyped fallback isn't a wire-mapped type, so it's excluded.
        _byWire = typeof(Component).Assembly
            .GetTypes()
            .Where(type =>
                !type.IsAbstract && type != typeof(UnknownComponent) && type.IsSubclassOf(typeof(Component)))
            .ToDictionary(t => t.Name.ToSnakeCase(), type => type, StringComparer.Ordinal);
    }

    /// <summary>The engine wire names backed by a typed component (snake_case of each class name).</summary>
    public IEnumerable<string> Wires => _byWire.Keys;

    /// <summary>The typed component type for an engine wire name, or null when no typed class models it.</summary>
    public Type? TypeFor(string wire) => _byWire.TryGetValue(wire, out var type) ? type : null;

    /// <summary>Whether a typed component models the given engine wire name.</summary>
    public bool IsTyped(string wire) => _byWire.ContainsKey(wire);
}
