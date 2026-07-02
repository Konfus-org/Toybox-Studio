using System;

namespace Toybox.Studio.EngineApi;

/// <summary>
/// Marks a backing field whose public, engine-synced property the EngineSync source generator emits — the
/// editor-side analogue of <c>CommunityToolkit.Mvvm</c>'s <c>[ObservableProperty]</c>, except the generated setter
/// also syncs the change to the engine (per <see cref="Mode"/>) on top of raising <c>PropertyChanged</c>. The
/// field's wire name defaults to its own name (leading underscore stripped, snake_cased) and reconciles against the
/// engine's <c>describe</c> at load; override it with <paramref name="wire"/> when convention can't produce the
/// engine's name. A <paramref name="converter"/> (an <see cref="IEngineSyncConverter{T}"/> type) overrides the default
/// value (de)serialization for a binary or odd-shaped field.
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class EngineSyncAttribute(
    string? wire = null, Type? converter = null, EngineSyncMode mode = EngineSyncMode.OnChanged, int windowMs = 0) : Attribute
{
    /// <summary>An explicit engine wire name, overriding the field-name convention; null uses the convention.</summary>
    public string? Wire { get; } = wire;

    /// <summary>An <see cref="IEngineSyncConverter{T}"/> type overriding the default (de)serialization, or null.</summary>
    public Type? Converter { get; } = converter;

    /// <summary>When/how the field's change syncs to the engine.</summary>
    public EngineSyncMode Mode { get; } = mode;

    /// <summary>The debounce window in milliseconds for <see cref="EngineSyncMode.Timer"/>, the throttle window for
    /// <see cref="EngineSyncMode.OnChanged"/> (floored at the scheduler's enforced minimum), and the re-pull interval for
    /// <see cref="EngineSyncMode.Stream"/>; ignored otherwise.</summary>
    public int WindowMs { get; } = windowMs;
}
