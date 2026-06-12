using Avalonia.Controls;

namespace Toybox.Studio.Widgets.Viewport;

public partial class ViewportView : UserControl
{
    public ViewportView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is ViewportViewModel viewModel)
                viewModel.FrameRendered += () => FrameImage.InvalidateVisual();
        };
    }
}
