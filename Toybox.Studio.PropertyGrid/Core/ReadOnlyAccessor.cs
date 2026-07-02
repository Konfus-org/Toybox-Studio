namespace Toybox.Studio.PropertyGrid;

/// <summary>
/// Wraps another <see cref="IValueAccessor"/> to strip its ability to persist: <see cref="CanWrite"/> reports
/// false and <see cref="Commit"/> is a no-op, while reads and structural navigation pass straight through. The
/// factory applies it to a <c>[[readonly]]</c> descriptor so an editable leaf both disables its control (via
/// <see cref="PropertyViewModel.IsReadOnly"/>) and never persists an in-place edit — the successor to the old
/// "withhold the commit action for a read-only field" rule, expressed on the accessor instead of a separate
/// commit parameter.
/// </summary>
public sealed class ReadOnlyAccessor(IValueAccessor inner) : IValueAccessor
{
    public Type ValueType => inner.ValueType;

    public bool CanWrite => false;

    public object? Get() => inner.Get();

    public void Set(object? value) => inner.Set(value);

    public void Commit()
    {
        // A read-only value never persists.
    }

    public IValueAccessor Member(PropertyDescriptor member) => new ReadOnlyAccessor(inner.Member(member));

    public IValueAccessor Element(int index) => new ReadOnlyAccessor(inner.Element(index));

    public object? CreateElement() => inner.CreateElement();
}
