using Avalonia.Controls;

namespace Toybox.Studio.Widgets.EngineViewport;

public partial class EngineViewportView : UserControl
{
    public EngineViewportView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is EngineViewportViewModel viewModel)
                viewModel.FrameRendered += () => FrameImage.InvalidateVisual();
        };
    }
}
