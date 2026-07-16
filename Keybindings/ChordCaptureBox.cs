using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia;
using Toybox.Studio.EngineApi.Types.Assets;

namespace Toybox.Studio.Keybindings;

/// <summary>
/// The press-a-key capture control: shows the bound <see cref="Chord"/> as its label; clicking arms
/// capture ("Press a chord…") and the next non-modifier key — with whatever modifiers are held —
/// becomes the chord. Esc cancels, Backspace/Delete unbinds, losing focus cancels. While capturing it
/// is an <see cref="IKeyCaptureSurface"/>, so the window's keybinding dispatcher stands down and the
/// chord being assigned can't invoke whatever it is currently bound to.
/// </summary>
public sealed class ChordCaptureBox : Button, IKeyCaptureSurface
{
    public static readonly StyledProperty<KeyChordInputControl?> ChordProperty =
        AvaloniaProperty.Register<ChordCaptureBox, KeyChordInputControl?>(
            nameof(Chord), defaultBindingMode: BindingMode.TwoWay);

    private bool _capturing;

    static ChordCaptureBox() =>
        ChordProperty.Changed.AddClassHandler<ChordCaptureBox>((box, _) => box.UpdateLabel());

    public ChordCaptureBox()
    {
        UpdateLabel();
        LostFocus += (_, _) => EndCapture();
    }

    // Style as a plain button; the label is the whole look.
    protected override Type StyleKeyOverride => typeof(Button);

    public KeyChordInputControl? Chord
    {
        get => GetValue(ChordProperty);
        set => SetValue(ChordProperty, value);
    }

    public bool IsCapturingKeys => _capturing;

    protected override void OnClick()
    {
        base.OnClick();
        _capturing = true;
        UpdateLabel();
        Focus();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!_capturing)
        {
            base.OnKeyDown(e);
            return;
        }

        e.Handled = true;
        if (e.Key == Key.Escape)
        {
            EndCapture();
            return;
        }

        if (e.Key is Key.Back or Key.Delete)
        {
            _capturing = false;
            Chord = null;
            // Re-assigning an unchanged value raises no property change, so settle the label here.
            UpdateLabel();
            return;
        }

        // A bare modifier press keeps waiting for the chord's key.
        if (KeyChordGestures.FromKey(e.Key, e.KeyModifiers) is { } chord)
        {
            _capturing = false;
            Chord = chord;
            UpdateLabel();
        }
    }

    private void EndCapture()
    {
        _capturing = false;
        UpdateLabel();
    }

    private void UpdateLabel() => Content = _capturing
        ? "Press a chord…"
        : Chord is { } chord
            ? KeyChordGestures.ToDisplayString(chord)
            : "Unbound";
}
