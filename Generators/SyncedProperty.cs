using System.Collections.Generic;

namespace Toybox.Studio.Generators;

/// <summary>Everything the emitter needs for one engine-synced partial property.</summary>
internal sealed class SyncedProperty(
    string name,
    string accessibility,
    string typeDisplay,
    bool isReferenceType,
    string command,
    string key,
    string mode,
    int batchFrequencyMs,
    bool isReadOnly,
    bool hasSetter,
    string setterModifier,
    string? converterDisplay,
    string writeCall,
    string readCall,
    bool isChildBearing,
    bool isChildCollection,
    List<SyncedExtra> extras)
{
    public string Name { get; } = name;

    public string Accessibility { get; } = accessibility;

    /// <summary>The fully-qualified property type, nullability included (partial signatures must match).</summary>
    public string TypeDisplay { get; } = typeDisplay;

    public bool IsReferenceType { get; } = isReferenceType;

    public string Command { get; } = command;

    public string Key { get; } = key;

    /// <summary>The SyncMode member name (e.g. "Batched").</summary>
    public string Mode { get; } = mode;

    public int BatchFrequencyMs { get; } = batchFrequencyMs;

    /// <summary>A OneWayFromEngine property never pushes; declared get-only, or with a private setter so
    /// the class can assign its own engine-owned state locally.</summary>
    public bool IsReadOnly { get; } = isReadOnly;

    public bool HasSetter { get; } = hasSetter;

    /// <summary>The setter's accessibility prefix when it differs from the property's
    /// (e.g. <c>"private "</c>), empty otherwise.</summary>
    public string SetterModifier { get; } = setterModifier;

    /// <summary>The fully-qualified converter type, when the member overrides the built-in codec.</summary>
    public string? ConverterDisplay { get; } = converterDisplay;

    /// <summary>The slot's write expression over the boxed <c>value</c> parameter.</summary>
    public string WriteCall { get; } = writeCall;

    /// <summary>The slot's read expression over the <c>token</c> parameter.</summary>
    public string ReadCall { get; } = readCall;

    /// <summary>Whether the property's value is (or contains) nested <see cref="EngineObject"/>s the
    /// owner binds and aggregates — an entity's components, a world's entities.</summary>
    public bool IsChildBearing { get; } = isChildBearing;

    /// <summary>Whether the child-bearing value is a collection of EngineObjects (vs. a single one), so
    /// the emitted <c>CollectChildren</c> iterates it.</summary>
    public bool IsChildCollection { get; } = isChildCollection;

    public List<SyncedExtra> Extras { get; } = extras;

    public string FieldName => "_" + char.ToLowerInvariant(Name[0]) + Name.Substring(1);

    public string SlotName => Name + "SyncSlot";

    public string ConverterFieldName => Name + "SyncConverter";
}
