using Avalonia.Media;
using System.Globalization;

namespace Toybox.Studio.PropertyGrid.Slots;

/// <summary>
/// Edits an <see cref="Color"/> as a swatch that opens R / G / B / A channel sliders plus a
/// <c>#RRGGBBAA</c> hex field. The channels read and write the accessor's colour directly (recomposed on
/// each change), so a reset or any other write is reflected the moment the accessor announces it.
/// </summary>
public sealed class ColorValueViewModel : ValueViewModel
{
    public ColorValueViewModel(PropertyValueAccessor accessor)
        : base(accessor)
        => accessor.Changed += RaiseAll;

    public int R
    {
        get => Current.R;
        set => Compose(a: Current.A, r: (byte)value, g: Current.G, b: Current.B);
    }

    public int G
    {
        get => Current.G;
        set => Compose(a: Current.A, r: Current.R, g: (byte)value, b: Current.B);
    }

    public int B
    {
        get => Current.B;
        set => Compose(a: Current.A, r: Current.R, g: Current.G, b: (byte)value);
    }

    public int A
    {
        get => Current.A;
        set => Compose(a: (byte)value, r: Current.R, g: Current.G, b: Current.B);
    }

    /// <summary>The colour as <c>#RRGGBBAA</c> (alpha last), round-tripping the channel fields.</summary>
    public string Hex
    {
        get => $"#{Current.R:X2}{Current.G:X2}{Current.B:X2}{Current.A:X2}";
        set
        {
            if (TryParseHex(value, out var color))
                Accessor.Set(color);
        }
    }

    /// <summary>The swatch fill for the trigger button.</summary>
    public IBrush Swatch => new SolidColorBrush(Current);

    private Color Current => Accessor.Get() as Color? ?? Colors.White;

    private void Compose(byte a, byte r, byte g, byte b)
    {
        if (!IsReadOnly)
            Accessor.Set(Color.FromArgb(a, r, g, b));
    }

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(R));
        OnPropertyChanged(nameof(G));
        OnPropertyChanged(nameof(B));
        OnPropertyChanged(nameof(A));
        OnPropertyChanged(nameof(Hex));
        OnPropertyChanged(nameof(Swatch));
    }

    // Parses "#RRGGBB" or "#RRGGBBAA" (alpha last, missing alpha defaults opaque); anything else fails.
    private static bool TryParseHex(string? text, out Color color)
    {
        color = Colors.White;
        var hex = text?.Trim().TrimStart('#');
        if (hex is not { Length: 6 or 8 })
            return false;

        if (!byte.TryParse(hex.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)
            || !byte.TryParse(hex.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)
            || !byte.TryParse(hex.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
        {
            return false;
        }

        byte a = 255;
        if (hex.Length == 8
            && !byte.TryParse(hex.AsSpan(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out a))
        {
            return false;
        }

        color = Color.FromArgb(a, r, g, b);
        return true;
    }
}
