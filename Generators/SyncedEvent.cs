namespace Toybox.Studio.Generators;

/// <summary>Everything the emitter needs for one engine-synced partial event.</summary>
internal sealed class SyncedEvent(
    string name,
    string accessibility,
    string delegateDisplay,
    string key,
    string? converterDisplay,
    string readCall)
{
    public string Name { get; } = name;

    public string Accessibility { get; } = accessibility;

    /// <summary>The fully-qualified delegate type (partial signatures must match).</summary>
    public string DelegateDisplay { get; } = delegateDisplay;

    /// <summary>The wire key inbound raises route by (camelCase member name unless overridden).</summary>
    public string Key { get; } = key;

    /// <summary>The fully-qualified converter type, when the member overrides the built-in codec.</summary>
    public string? ConverterDisplay { get; } = converterDisplay;

    /// <summary>The slot's read expression over the <c>token</c> parameter.</summary>
    public string ReadCall { get; } = readCall;

    public string FieldName => "_" + char.ToLowerInvariant(Name[0]) + Name.Substring(1);

    public string SlotName => Name + "SyncEventSlot";

    public string ConverterFieldName => Name + "SyncEventConverter";
}
