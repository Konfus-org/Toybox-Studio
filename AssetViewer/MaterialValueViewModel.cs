using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Numerics;
using Toybox.Studio.EngineApi.Types.Assets;
using Toybox.Studio.PropertyGrid.Slots;
using Toybox.Studio.PropertyGrid;
using Toybox.Studio.Utils.Composition;

namespace Toybox.Studio.AssetViewer;

/// <summary>
/// Edits a shader parameter's <see cref="MaterialValue"/>: a kind selector plus the matching value
/// editor for the chosen kind, reusing the grid's own built-in editors (bool / number / vector / colour)
/// through a child accessor that rebuilds the value on each edit. Switching the kind resets the value to
/// that kind's default. An <see cref="MaterialValueKind.Unknown"/> shape (a matrix) has no editor and
/// shows read-only. This is the type-driven seam that replaces Studio 1.0's bespoke parameter rows.
/// </summary>
public sealed partial class MaterialValueViewModel : ValueViewModel
{
    // Set while a nested editor writes through us, so the resulting accessor change doesn't tear the
    // editor down and rebuild it mid-edit; an external change (or a kind switch) is not guarded and does.
    private bool _applying;

    private readonly ViewModelFactory _viewModels;

    public MaterialValueViewModel(PropertyValueAccessor accessor, ViewModelFactory viewModels)
        : base(accessor)
    {
        _viewModels = viewModels;
        BuildEditor();
        accessor.Changed += OnAccessorChanged;
    }

    public IReadOnlyList<MaterialValueKind> Kinds { get; } = Enum.GetValues<MaterialValueKind>();

    public MaterialValueKind Kind
    {
        get => Current.Kind;
        set
        {
            if (value != Current.Kind && !IsReadOnly)
                Accessor.Set(MaterialValue.Default(value));
        }
    }

    /// <summary>The value editor for the current kind (null for an unknown shape, shown read-only).</summary>
    [ObservableProperty]
    public partial ValueViewModel? Editor { get; set; }

    private MaterialValue Current => Accessor.Get() as MaterialValue ?? new MaterialValue();

    private void OnAccessorChanged()
    {
        // A nested-editor value write keeps the same editor (it is the source of the change); a kind
        // switch or an external reset rebuilds it and refreshes the selector.
        if (_applying)
            return;

        OnPropertyChanged(nameof(Kind));
        BuildEditor();
    }

    private void BuildEditor() => Editor = Current.Kind switch
    {
        MaterialValueKind.Bool => _viewModels.Create<BoolValueViewModel>(
            Child(value => value.BoolValue, (value, edited) => value with { BoolValue = (bool)edited! })),
        MaterialValueKind.Int => _viewModels.Create<NumberValueViewModel>(
            Child(value => value.IntValue, (value, edited) => value with { IntValue = Convert.ToInt32(edited) }),
            typeof(int)),
        MaterialValueKind.Float => _viewModels.Create<NumberValueViewModel>(
            Child(value => value.FloatValue, (value, edited) => value with { FloatValue = Convert.ToSingle(edited) }),
            typeof(float)),
        MaterialValueKind.Vector2 => _viewModels.Create<VectorValueViewModel>(
            Child(value => value.Vector2Value, (value, edited) => value with { Vector2Value = (Vector2)edited! }), 2),
        MaterialValueKind.Vector3 => _viewModels.Create<VectorValueViewModel>(
            Child(value => value.Vector3Value, (value, edited) => value with { Vector3Value = (Vector3)edited! }), 3),
        MaterialValueKind.Vector4 => _viewModels.Create<VectorValueViewModel>(
            Child(value => value.Vector4Value, (value, edited) => value with { Vector4Value = (Vector4)edited! }), 4),
        MaterialValueKind.Color => _viewModels.Create<ColorValueViewModel>(
            Child(value => value.ColorValue, (value, edited) => value with { ColorValue = (Color)edited! })),
        _ => null,
    };

    // A cursor over one typed field of the value: reads the field, and writes a rebuilt value back
    // through our accessor (guarded so it doesn't rebuild the nested editor beneath the user).
    private PropertyValueAccessor Child(
        Func<MaterialValue, object?> read, Func<MaterialValue, object?, MaterialValue> write) =>
        new(
            () => read(Current),
            edited => Apply(() => Accessor.Set(write(Current, edited))));

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
}
