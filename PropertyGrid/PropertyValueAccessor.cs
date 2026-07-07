namespace Toybox.Studio.PropertyGrid;

/// <summary>
/// The read/write cursor a leaf row's parts share: the value part edits through it, the state indicator
/// reads read-only/default state and resets through it. Writes announce themselves via
/// <see cref="Changed"/>, so every part on the row (and, via the factory's wiring, the owning node)
/// reacts to any part's edit — a reset updates the editor, an edit updates the state dot.
/// </summary>
public sealed class PropertyValueAccessor
{
    private readonly Func<object?> _get;
    private readonly Action<object?>? _set;

    /// <param name="set">Null when the value is read-only.</param>
    public PropertyValueAccessor(Func<object?> get, Action<object?>? set)
    {
        _get = get;
        _set = set;
    }

    /// <param name="set">Null when the value is read-only.</param>
    /// <param name="defaultValue">The value's default, enabling the default/modified state and reset.</param>
    public PropertyValueAccessor(Func<object?> get, Action<object?>? set, object? defaultValue)
        : this(get, set)
    {
        DefaultValue = defaultValue;
        HasDefault = true;
    }

    /// <summary>Raised after a write actually changed the value.</summary>
    public event Action? Changed;

    public bool IsReadOnly => _set is null;

    /// <summary>Whether a default is known — without one there is no default/modified state or reset.</summary>
    public bool HasDefault { get; }

    public object? DefaultValue { get; }

    public bool IsDefault => HasDefault && Equals(_get(), DefaultValue);

    public object? Get() => _get();

    public void Set(object? value)
    {
        if (_set is null || Equals(_get(), value))
            return;

        _set(value);
        Changed?.Invoke();
    }
}
