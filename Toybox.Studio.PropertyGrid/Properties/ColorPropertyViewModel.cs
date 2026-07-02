using System.ComponentModel;
using Avalonia.Media;
using Toybox.Studio.Theming;
using Toybox.Studio.ColorPickers;

namespace Toybox.Studio.PropertyGrid;

/// <summary>
/// Colour property: edits the engine's colour value through the shared <see cref="ColorGradientView"/> in its
/// solid-only mode, so colours in the grid use the same colour editor as the rest of the app. Each edit writes
/// the whole <see cref="Color"/> back through the accessor (the conversion to the engine's <c>{r,g,b,a}</c>
/// 0..1 float shape lives in <see cref="Toybox.Studio.EngineApi.EngineSyncValue"/>), so the host's
/// commit re-sends the colour. Engine colours are flat, so the editor is locked to a solid colour (no gradient).
/// </summary>
public sealed partial class ColorPropertyViewModel : PropertyViewModel
{
    public ColorPropertyViewModel(PropertyDescriptor descriptor, IValueAccessor accessor)
        : base(descriptor, accessor)
    {
        var color = accessor.Get() as Color? ?? Avalonia.Media.Colors.White;
        Editor = new ColorGradientViewModel(ColorGradient.Solid(color), solidOnly: true);
        Editor.PropertyChanged += OnEditorChanged;
    }

    /// <summary>The shared colour editor (solid-only) bound by the editable view.</summary>
    public ColorGradientViewModel Editor { get; }

    /// <summary>The current colour, taken from the editor's single stop.</summary>
    private Color Color => Editor.Start;

    // Track a running game's live colour in place rather than letting the base DeepEquals check fail and force
    // a grid rebuild, which would tear down an open colour picker mid-edit. Push the fresh colour into the
    // editor; setting Start raises OnEditorChanged, whose write-back is suppressed during Sync (RaiseCommit
    // no-ops), so the engine isn't re-sent its own value.
    protected override bool SyncCore(IValueAccessor accessor)
    {
        Editor.Start = accessor.Get() as Color? ?? Avalonia.Media.Colors.White;
        return true;
    }

    /// <summary>Hex readout (#RRGGBBAA) of the current colour, for the read-only display.</summary>
    public string Hex => $"#{Color.R:X2}{Color.G:X2}{Color.B:X2}{Color.A:X2}";

    /// <summary>Swatch brush of the current colour, for the read-only display.</summary>
    public IBrush Swatch => new SolidColorBrush(Color);

    private void OnEditorChanged(object? sender, PropertyChangedEventArgs args)
    {
        // Only the colour stop matters here; the editor is locked solid so the gradient fields never move.
        if (args.PropertyName != nameof(ColorGradientViewModel.Start))
            return;

        OnPropertyChanged(nameof(Hex));
        OnPropertyChanged(nameof(Swatch));

        if (IsReadOnly)
            return;

        Accessor.Set(Color);
        RaiseCommit();
    }
}
