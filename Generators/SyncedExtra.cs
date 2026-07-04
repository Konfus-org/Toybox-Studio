namespace Toybox.Studio.Generators;

/// <summary>
/// One resolved entry from an [EngineSync] <c>args</c> tail: the payload key and the complete C#
/// expression producing its value — a literal for <c>"key=constant"</c> entries, a codec call over the
/// named member for <c>nameof(Member)</c> entries.
/// </summary>
internal sealed class SyncedExtra(string key, string valueExpression)
{
    public string Key { get; } = key;

    public string ValueExpression { get; } = valueExpression;
}
