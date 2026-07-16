using System.Text.Json;

namespace Toybox.Studio.PropertyGrid.Slots;

/// <summary>
/// The fallback value editor for a property whose type the grid has no fancy view for: it shows the value
/// as indented JSON and writes edits straight back by deserializing to the property's type. Unlike the
/// read-only ToString fallback it replaces, the field is editable — the draft validates as you type, and
/// malformed JSON (or JSON that doesn't fit the type) reddens the field and surfaces the parser's complaint
/// through <see cref="Error"/> instead of committing, so the underlying value only changes when the text is
/// valid. A read-only property (no setter) still shows its JSON, disabled.
/// </summary>
public sealed class UnknownValueViewModel : ValueViewModel
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly Type _valueType;

    // The editable text buffer. Unlike the other editors this can't read straight from the accessor: while
    // the JSON is malformed there is no value to round-trip through, so the invalid text must be kept here.
    private string _draft;
    private string? _error;

    // True only while our own commit writes through the accessor, so the resulting Changed doesn't reformat
    // the draft out from under the user mid-edit.
    private bool _committing;

    public UnknownValueViewModel(PropertyValueAccessor accessor, Type valueType) : base(accessor)
    {
        _valueType = valueType;
        _draft = Serialize(accessor.Get());
        // An external write (a reset, a sibling edit, a paste) swaps the value under us — resync the draft to
        // it and clear any stale error, unless the write was our own commit (which already matches the draft).
        accessor.Changed += Resync;
    }

    // Named Value so the base's accessor-change notification (fired as "Value") reaches the field.
    public string Value
    {
        get => _draft;
        set
        {
            _draft = value;
            Commit();
        }
    }

    /// <summary>Whether the draft is well-formed JSON for the type — false reddens the field and surfaces
    /// <see cref="Error"/>.</summary>
    public bool IsValid => _error is null;

    /// <summary>The parser's complaint for a malformed draft, or null when it is valid.</summary>
    public string? Error => _error;

    private void Commit()
    {
        object? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize(_draft, _valueType, Options);
            _error = null;
        }
        catch (Exception exception)
        {
            // A malformed draft (or one a converter refuses) must never crash the grid — surface the message
            // and hold the edit back until the text is valid.
            _error = exception.Message;
            RefreshValidity();
            return;
        }

        _committing = true;
        try
        {
            Accessor.Set(parsed);
        }
        finally
        {
            _committing = false;
        }

        RefreshValidity();
    }

    private void Resync()
    {
        if (_committing)
            return;

        _draft = Serialize(Accessor.Get());
        _error = null;
        OnPropertyChanged(nameof(Value));
        RefreshValidity();
    }

    private void RefreshValidity()
    {
        OnPropertyChanged(nameof(IsValid));
        OnPropertyChanged(nameof(Error));
    }

    private string Serialize(object? value)
    {
        if (value is null)
            return "null";

        try
        {
            return JsonSerializer.Serialize(value, _valueType, Options);
        }
        catch (Exception exception)
        {
            // A value the serializer can't render (a reference cycle) still gets an editable — if unhelpful —
            // starting point rather than taking the grid down.
            return $"/* {_valueType.Name} can't be shown as JSON: {exception.Message} */";
        }
    }
}
