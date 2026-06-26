using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Toybox.Studio.Services.Clipboard;

/// <summary>
/// A generic, JSON-backed clipboard over the OS clipboard. Any class or struct can be <see cref="PushAsync"/>ed
/// and later read back into the runtime type of your choice via the <c>&lt;T&gt;</c> endpoints
/// (<see cref="PeekAsync"/> reads without consuming, <see cref="PopAsync"/> reads and clears,
/// <see cref="ViewAsync"/> reports what kind is held without deserializing). The value is serialized into a
/// tagged envelope — <c>{ toyboxClipboard: &lt;kind&gt;, payload: … }</c> — where the kind comes from the type's
/// <see cref="ClipboardKindAttribute"/> (or its type name), so a Peek/Pop only deserializes a payload that
/// actually matches the requested type and arbitrary clipboard text that isn't ours is ignored. Storing on the
/// system clipboard (rather than an in-memory slot) lets a copy survive focus loss and paste between Studio
/// instances, and leaves the payload inspectable.
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

    /// <summary>Serializes <paramref name="value"/> under its kind tag and writes it to the clipboard.</summary>
    public Task PushAsync<T>(T value)
        where T : notnull
    {
        var envelope = new JObject
        {
            [KindKey] = KindOf<T>(),
            [PayloadKey] = JToken.FromObject(value),
        };
        return WriteAsync(envelope);
    }

    /// <summary>
    /// Deserializes the current clipboard payload into <typeparamref name="T"/> when it holds one, without
    /// clearing it. Returns <c>default</c> (null for a reference type) when the clipboard is empty or holds a
    /// different kind — call <see cref="HasAsync{T}"/> first to disambiguate for a value type.
    /// </summary>
    public async Task<T?> PeekAsync<T>()
    {
        var envelope = await ReadAsync().ConfigureAwait(false);
        return Deserialize<T>(envelope);
    }

    /// <summary>
    /// Like <see cref="PeekAsync{T}"/>, but clears the clipboard when it matched — consuming the item. The
    /// clipboard is a single slot, so "pop" is read-then-clear rather than a stack pop.
    /// </summary>
    public async Task<T?> PopAsync<T>()
    {
        var envelope = await ReadAsync().ConfigureAwait(false);
        if (!Matches<T>(envelope))
            return default;

        var value = Deserialize<T>(envelope);
        await ClearAsync().ConfigureAwait(false);
        return value;
    }

    /// <summary>The kind tag currently on the clipboard (null when empty or not ours), without deserializing.</summary>
    public async Task<string?> ViewAsync()
    {
        var envelope = await ReadAsync().ConfigureAwait(false);
        return envelope?.Value<string>(KindKey);
    }

    /// <summary>Whether the clipboard currently holds a <typeparamref name="T"/> — the test before a value-type Pop.</summary>
    public async Task<bool> HasAsync<T>() =>
        await ViewAsync().ConfigureAwait(false) == KindOf<T>();

    /// <summary>Empties the clipboard.</summary>
    public async Task ClearAsync()
    {
        if (Os is { } clipboard)
            await clipboard.ClearAsync().ConfigureAwait(false);
    }

    // Deserializes the payload into T when the envelope's kind matches; default otherwise (or on a bad payload).
    private static T? Deserialize<T>(JObject? envelope)
    {
        if (!Matches<T>(envelope) || envelope![PayloadKey] is not { } payload)
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

    private static bool Matches<T>(JObject? envelope) =>
        envelope?.Value<string>(KindKey) == KindOf<T>();

    // The wire tag for a type: its [ClipboardKind] if present, else its plain type name.
    private static string KindOf<T>() =>
        typeof(T).GetCustomAttribute<ClipboardKindAttribute>()?.Kind ?? typeof(T).Name;

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
