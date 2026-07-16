using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using System.Linq;

namespace Toybox.Studio.Generators;

/// <summary>
/// One [EngineSync] attribute's settings as written, plus the merge rules: a member's config fills its
/// gaps from the nearest class-level config up the base chain (command, mode, batch frequency, and —
/// when the member adds none of its own — the wire payload entries).
/// </summary>
internal sealed class SyncAttributeConfig
{
    private SyncAttributeConfig()
    {
    }

    public string? Command { get; private set; }

    public int? Mode { get; private set; }

    public INamedTypeSymbol? Converter { get; private set; }

    /// <summary>The Batched frequency in milliseconds; null when unset (zero counts as unset — it
    /// means "the scheduler's floor" and lets a class-level default through).</summary>
    public int? BatchFrequencyMs { get; private set; }

    /// <summary>The raw engineParams values: strings (member refs, keys, <c>"key=text"</c>) and the
    /// boxed typed constants that follow their keys.</summary>
    public List<object?> EngineParams { get; private set; } = [];

    /// <summary>The class-level address template; never inherited — each family root declares its own.</summary>
    public string? Address { get; private set; }

    public string? Key { get; private set; }

    public bool Relay { get; private set; }

    public Location? Location { get; private set; }

    /// <summary>Reads one attribute application; null when it isn't [EngineSync].</summary>
    public static SyncAttributeConfig? From(AttributeData attribute)
    {
        if (attribute.AttributeClass?.ToDisplayString() != EngineSyncGenerator.AttributeName)
            return null;

        var config = new SyncAttributeConfig
        {
            Location = attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation(),
        };

        var parameters = attribute.AttributeConstructor?.Parameters ?? [];
        for (var i = 0; i < parameters.Length && i < attribute.ConstructorArguments.Length; i++)
        {
            var argument = attribute.ConstructorArguments[i];
            switch (parameters[i].Name)
            {
                case "syncCommand":
                    config.Command = argument.Value as string;
                    break;
                case "syncMode":
                    config.Mode = argument.Value as int?;
                    break;
                case "converter":
                    config.Converter = argument.Value as INamedTypeSymbol;
                    break;
                case "batchFrequencyMs":
                    if (argument.Value is int frequency and > 0)
                        config.BatchFrequencyMs = frequency;
                    break;
                case "engineParams":
                    config.EngineParams = [.. argument.Values.Select(value => value.Value)];
                    break;
            }
        }

        foreach (var (name, value) in attribute.NamedArguments.Select(pair => (pair.Key, pair.Value)))
        {
            switch (name)
            {
                case "Mode":
                    config.Mode = value.Value as int?;
                    break;
                case "Converter":
                    config.Converter = value.Value as INamedTypeSymbol;
                    break;
                case "Address":
                    config.Address = value.Value as string;
                    break;
                case "Key":
                    config.Key = value.Value as string;
                    break;
                case "Relay":
                    config.Relay = value.Value is true;
                    break;
            }
        }

        return config;
    }

    /// <summary>The [EngineSync] attribute on <paramref name="symbol"/> itself, or null.</summary>
    public static SyncAttributeConfig? On(ISymbol symbol) =>
        symbol.GetAttributes().Select(From).FirstOrDefault(config => config is not null);

    /// <summary>The nearest class-level config, walking the base chain from <paramref name="type"/>.</summary>
    public static SyncAttributeConfig? ClassDefaults(INamedTypeSymbol? type)
    {
        for (var current = type; current is not null; current = current.BaseType)
            if (On(current) is { } config)
                return config;
        return null;
    }

    /// <summary>This config with its gaps filled from <paramref name="defaults"/>.</summary>
    public SyncAttributeConfig MergedWith(SyncAttributeConfig? defaults)
    {
        if (defaults is null)
            return this;

        Command ??= defaults.Command;
        Mode ??= defaults.Mode;
        BatchFrequencyMs ??= defaults.BatchFrequencyMs;
        if (EngineParams.Count == 0)
            EngineParams = defaults.EngineParams;
        return this;
    }
}
