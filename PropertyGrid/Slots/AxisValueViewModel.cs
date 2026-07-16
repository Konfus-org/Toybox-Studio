using System.Collections.ObjectModel;

namespace Toybox.Studio.PropertyGrid.Slots;

/// <summary>
/// The shared parent of the multi-field editors — a vector and a Euler rotation — that present a value
/// as a row of labeled scalar <see cref="VectorAxisViewModel"/> fields. A field edit rebuilds the whole
/// composite and writes it through the shared accessor; an external change to the accessor (a reset, a
/// live sync) re-reads it into the fields. A field's own write is suppressed from re-reading itself, so
/// the other fields never jump to an equivalent-but-different representation while one is being typed
/// (which matters for the rotation's Euler ↔ quaternion round-trip).
/// </summary>
public abstract class AxisValueViewModel : ValueViewModel
{
    private bool _applying;

    protected AxisValueViewModel(PropertyValueAccessor accessor)
        : base(accessor)
        => accessor.Changed += OnAccessorChanged;

    public ObservableCollection<VectorAxisViewModel> Components { get; } = [];

    /// <summary>Adds a field: its display value and the write that rebuilds the composite from an edit to
    /// it (wrapped so the resulting accessor change doesn't re-read this same field mid-edit).</summary>
    protected void AddAxis(string label, double value, Action<double> write) =>
        Components.Add(new VectorAxisViewModel(label, (decimal)value, edited => Apply(() => write(edited))));

    /// <summary>Re-reads the composite from the accessor into the fields (an external change).</summary>
    protected abstract void SyncFromValue();

    private void Apply(Action write)
    {
        _applying = true;
        try
        {
            write();
        }
        finally
        {
            _applying = false;
        }
    }

    private void OnAccessorChanged()
    {
        if (!_applying)
            SyncFromValue();
    }
}
