using Newtonsoft.Json.Linq;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// A mutable handle to a single leaf value inside the backing JSON document. Editing a value with
/// <see cref="JToken.Replace"/> detaches the old node, so a widget must re-hold the replacement to keep
/// mutating the live document on the next edit — a contract that is easy to get wrong when each widget
/// writes it by hand (a forgotten re-hold silently stops persisting). This wrapper owns the
/// replace-and-re-hold so widgets just call <see cref="Set"/>.
/// </summary>
public sealed class JsonValueSlot
{
    private JToken? _token;

    public JsonValueSlot(JToken? token) => _token = token;

    /// <summary>True when there is a live token to write to (false for an absent or read-only value).</summary>
    public bool IsBound => _token is not null;

    /// <summary>Reads the current value as <typeparamref name="T"/>, or <c>default</c> when unbound.</summary>
    public T? Read<T>() => _token is null ? default : _token.Value<T>();

    /// <summary>
    /// Replaces the live value in place and re-holds the replacement so the next edit still persists.
    /// No-op when unbound. Returns true when a write actually happened.
    /// </summary>
    public bool Set(JValue value)
    {
        if (_token is null)
            return false;

        _token.Replace(value);
        _token = value;
        return true;
    }
}
