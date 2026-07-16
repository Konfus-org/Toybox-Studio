using CommunityToolkit.Mvvm.ComponentModel;

namespace Toybox.Studio.PropertyGrid.Slots;

/// <summary>
/// One labeled scalar field (X / Y / Z / W) inside a vector or rotation editor. Editing it asks the
/// owning <see cref="AxisValueViewModel"/> (through the write callback) to rebuild the whole value and
/// push it — there is no per-component value to write on its own. A sync (the owner re-reading the
/// composite after an external change) moves the display WITHOUT re-triggering that write.
/// </summary>
public sealed partial class VectorAxisViewModel : ObservableObject
{
    private readonly Action<double> _write;
    private bool _syncing;

    [ObservableProperty]
    public partial decimal? Value { get; set; }

    public VectorAxisViewModel(string label, decimal value, Action<double> write)
    {
        Label = label;
        _write = write;

        // Seed the display through Sync so the initial value doesn't trigger a write-back: the field is
        // still being added to its owner (Components), so the sibling fields the write reads don't exist
        // yet — and seeding the current model value should never push an edit anyway.
        Sync(value);
    }

    /// <summary>The axis label (X / Y / Z / W); also the key its accent colour is chosen from.</summary>
    public string Label { get; }

    /// <summary>Moves the display to a fresh value without rebuilding the composite (a re-read sync).</summary>
    public void Sync(decimal value)
    {
        _syncing = true;
        try
        {
            Value = value;
        }
        finally
        {
            _syncing = false;
        }
    }

    partial void OnValueChanged(decimal? value)
    {
        if (value is null || _syncing)
            return;

        _write((double)value.Value);
    }
}
