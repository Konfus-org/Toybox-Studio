using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Toybox.Studio.Services.Clipboard;

/// <summary>
/// A generic, JSON-backed clipboard over the OS clipboard. Anything serializable can be <see cref="Copy{T}"/>d
/// and read back into the same type with <see cref="Paste{T}"/> — the engine speaks JSON, so an entity, a
/// component, or a single property value all travel the same path with no per-kind plumbing. The value is
/// serialized into a tagged envelope — <c>{ toyboxClipboard: &lt;kind&gt;, payload: … }</c> — where the kind
/// comes from the type's <see cref="ClipboardKindAttribute"/> (or its type name), optionally narrowed by a
/// <c>variant</c> (e.g. a property's type token, so a <c>Vector3</c> only pastes into a <c>Vector3</c>). A
/// <see cref="Paste{T}"/> only deserializes a payload whose kind matches the requested type+variant, so
/// arbitrary clipboard text that isn't ours — or a copy of the wrong kind — is ignored. Storing on the system
/// clipboard (rather than an in-memory slot) lets a copy survive focus loss and paste between Studio instances,
/// and leaves the payload inspectable.
/// </summary>
public sealed class Clipboard
{
    private const string KindKey = "toyboxClipboard";
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
    /// Whether the clipboard currently holds a <typeparamref name="T"/> of the given
    /// <paramref name="variant"/> — a cheap kind-tag check (no payload deserialize) for gating a Paste
    /// action's visibility.
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

    // The wire tag for a type: its [ClipboardKind] (or plain type name), optionally narrowed by a variant.
    private static string KindOf<T>(string? variant)
    {
        var kind = typeof(T).GetCustomAttribute<ClipboardKindAttribute>()?.Kind ?? typeof(T).Name;
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
