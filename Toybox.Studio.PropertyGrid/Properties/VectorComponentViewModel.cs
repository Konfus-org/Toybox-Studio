using CommunityToolkit.Mvvm.ComponentModel;

namespace Toybox.Studio.PropertyGrid;

/// <summary>
/// One labeled scalar inside a vector (X/Y/Z/W). Editing it asks the owning
/// <see cref="VectorPropertyViewModel"/> to rebuild the whole vector and write it back through the accessor —
/// there is no per-element JSON token to mutate.
/// </summary>
public sealed partial class VectorComponentViewModel : ObservableObject
{
    private readonly int _index;
    private readonly VectorPropertyViewModel _owner;

    // Set while the owner pushes a fresh value in (a live-value sync): move the display WITHOUT asking the owner
    // to rewrite the vector, mirroring the old in-place sync that left the commit suppressed.
    private bool _syncing;

    [ObservableProperty]
    private decimal? _value;

    public VectorComponentViewModel(string label, int index, decimal value, VectorPropertyViewModel owner)
    {
        Label = label;
        _index = index;
        _owner = owner;
        _value = value;
    }

    public string Label { get; }

    /// <summary>Moves the display to a fresh value without rewriting the vector (a live-value sync).</summary>
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

        _owner.SetComponent(_index, (double)value.Value);
    }
}
