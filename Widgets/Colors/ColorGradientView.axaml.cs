using Avalonia.Controls;

namespace Toybox.Studio.Widgets.Colors;

/// <summary>
/// Editor for a <see cref="ColorGradientViewModel"/>: a preview swatch, start/end colour pickers, a
/// Solid/Gradient toggle, and an angle slider. Pure view — all state lives in the bound view-model.
/// </summary>
public partial class ColorGradientView : UserControl
{
    public ColorGradientView()
    {
        InitializeComponent();
    }
}
