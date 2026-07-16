using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace Toybox.Studio.PropertyGrid.Slots;

/// <summary>
/// The parent of the view-models that fill a row's value slot. Each concrete editor pairs one value
/// shape with its view (text / number / bool / enum) through a <c>Value</c> property that reads and
/// writes the shared accessor directly — never a cached copy — so a reset (or any other slot's write)
/// is reflected the moment the accessor announces it.
/// </summary>
public abstract class ValueViewModel : ObservableObject
{
    // Every concrete editor names its editable property Value, so one change notification covers them all.
    private const string ValuePropertyName = "Value";

    protected ValueViewModel(PropertyValueAccessor accessor)
    {
        Accessor = accessor;
        accessor.Changed += () => OnPropertyChanged(ValuePropertyName);
    }

    public bool IsReadOnly => Accessor.IsReadOnly;

    /// <summary>The row's current value (boxed); null when unset. The context menu copies this.</summary>
    public object? CurrentValue => Accessor.Get();

    /// <summary>The value's CLR type, resolved from the live value or its default — the clipboard tags a copy
    /// with it so a value only pastes into a row of the same type. Null when neither is known.</summary>
    public Type? ValueType => (Accessor.Get() ?? Accessor.DefaultValue)?.GetType();

    /// <summary>Whether the row can be reset — it has a known default, is writable, and isn't already at it.</summary>
    public bool CanReset => Accessor.HasDefault && !Accessor.IsReadOnly && !Accessor.IsDefault;

    /// <summary>Resets the row to its default (no-op when it can't reset).</summary>
    public void ResetToDefault()
    {
        if (CanReset)
            Accessor.Set(Accessor.DefaultValue);
    }

    /// <summary>Writes a value through the accessor (a paste); ignored when read-only or unchanged.</summary>
    public void ApplyValue(object? value) => Accessor.Set(value);

    protected PropertyValueAccessor Accessor { get; }
}
