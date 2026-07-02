using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Clipboards;

/// <summary>
/// A generic, JSON-backed clipboard over the OS clipboard. Two shapes travel the same tagged envelope —
/// <c>{ toyboxClipboard: &lt;kind&gt;, name?, payload: … }</c>:
/// <list type="bullet">
/// <item>a plain serializable value (a property's <see cref="JToken"/>, an <c>AssetHandle</c>) via
/// <see cref="Copy{T}"/>/<see cref="Paste{T}"/> — round-tripped through Json.NET back into the same type;</item>
/// <item>any <see cref="ISerializable"/> (an entity, a component) via <see cref="CopyObject{T}"/>/<see cref="PasteObject{T}"/> —
/// stored as the object's <see cref="ISerializable.Serialize"/> body and applied back with
/// <see cref="ISerializable.Deserialize"/> onto a live handle, so no per-kind wrapper type is needed.</item>
/// </list>
/// The <c>kind</c> is the type's name, optionally narrowed by a <c>variant</c> (e.g. a property's type token, so a
/// <c>Vector3</c> only pastes into a <c>Vector3</c>). A <c>Paste</c> only reads a payload whose kind matches the
/// requested type, so arbitrary clipboard text that isn't ours — or a copy of the wrong kind — is ignored;
/// <see cref="Has{T}"/> is the cheap kind-tag check that gates a Paste action's visibility for either shape.
/// Storing on the system clipboard (rather than an in-memory slot) lets a copy survive focus loss and paste
/// between Studio instances, and leaves the payload inspectable.
/// </summary>
public sealed class Clipboard
{
    private const string KindKey = "toyboxClipboard";
    private const string NameKey = "name";
    private const string PayloadKey = "payload";

    // The clipboard hangs off the top-level window; this service has no visual of its own, so it reaches the
    // desktop lifetime's main window. Null before the window exists (it never is, in practice).
    private static IClipboard? Os =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
        ?.MainWindow?.Clipboard;

    /// <summary>
    /// Serializes <paramref name="value"/> under its kind tag and writes it to the clipboard. Pass
    /// <paramref name="variant"/> to narrow the tag (e.g. a property's type token) so only a matching
    /// <see cref="Paste{T}"/> reads it back.
    /// </summary>
    public Task Copy<T>(T value, string? variant = null)
        where T : notnull
    {
        var envelope = new JObject
        {
            [KindKey] = KindOf<T>(variant),
            [PayloadKey] = JToken.FromObject(value),
        };
        return WriteAsync(envelope);
    }

    /// <summary>
    /// Deserializes the current clipboard payload into <typeparamref name="T"/> when it holds one of that
    /// kind+<paramref name="variant"/>, without clearing it (so the same item can be pasted repeatedly).
    /// Returns <c>default</c> (null for a reference type) when the clipboard is empty or holds a different kind.
    /// </summary>
    public async Task<T?> Paste<T>(string? variant = null)
    {
        var envelope = await ReadAsync().ConfigureAwait(false);
        if (envelope?.Value<string>(KindKey) != KindOf<T>(variant) || envelope[PayloadKey] is not { } payload)
            return default;

        try
        {
            return payload.ToObject<T>();
        }
        catch (JsonException)
        {
            return default;
        }
    }

    /// <summary>
    /// Copies an <see cref="ISerializable"/>'s <see cref="ISerializable.Serialize"/> body under its type's kind,
    /// with an optional <paramref name="name"/> carried back alongside the body (a discriminator the body itself
    /// doesn't hold — e.g. a component's engine wire name, so the paste knows which slot to restore into).
    /// </summary>
    public Task CopyObject<T>(T value, string? name = null)
        where T : ISerializable
    {
        var envelope = new JObject
        {
            [KindKey] = KindOf<T>(null),
            [NameKey] = name,
            [PayloadKey] = value.Serialize(),
        };
        return WriteAsync(envelope);
    }

    /// <summary>
    /// The current clipboard's serialized body (and any <c>name</c>) when it holds a <typeparamref name="T"/>,
    /// without clearing it. Returns <c>null</c> when the clipboard is empty or holds a different kind. The caller
    /// applies the returned body with <see cref="ISerializable.Deserialize"/> onto a live handle.
    /// </summary>
    public async Task<(JObject Body, string? Name)?> PasteObject<T>()
        where T : ISerializable
    {
        var envelope = await ReadAsync().ConfigureAwait(false);
        if (envelope?.Value<string>(KindKey) != KindOf<T>(null) || envelope[PayloadKey] is not JObject body)
            return null;

        return (body, envelope.Value<string>(NameKey));
    }

    /// <summary>
    /// Whether the clipboard currently holds a <typeparamref name="T"/> of the given
    /// <paramref name="variant"/> — a cheap kind-tag check (no payload deserialize) for gating a Paste
    /// action's visibility. Gates both a <see cref="Copy{T}"/> value and a <see cref="CopyObject{T}"/> body.
    /// </summary>
    public async Task<bool> Has<T>(string? variant = null)
    {
        var envelope = await ReadAsync().ConfigureAwait(false);
        return envelope?.Value<string>(KindKey) == KindOf<T>(variant);
    }

    /// <summary>Empties the clipboard.</summary>
    public async Task Clear()
    {
        if (Os is { } clipboard)
            await clipboard.ClearAsync().ConfigureAwait(false);
    }

    // The wire tag for a type: its plain type name, optionally narrowed by a variant.
    private static string KindOf<T>(string? variant)
    {
        var kind = typeof(T).Name;
        return string.IsNullOrEmpty(variant) ? kind : $"{kind}:{variant}";
    }

    private static async Task WriteAsync(JObject envelope)
    {
        if (Os is { } clipboard)
            await clipboard.SetTextAsync(envelope.ToString(Formatting.Indented)).ConfigureAwait(false);
    }

    private static async Task<JObject?> ReadAsync()
    {
        if (Os is not { } clipboard)
            return null;

        var text = await clipboard.TryGetTextAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(text))
            return null;

        try
        {
            return JObject.Parse(text);
        }
        catch (JsonException)
        {
            // Not our JSON (or not JSON at all) — nothing to read.
            return null;
        }
    }
}
