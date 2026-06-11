using System.Collections.Specialized;
using Avalonia.Controls;

namespace Toybox.Studio.Widgets.EngineConsole;

public partial class EngineConsoleView : UserControl
{
    public EngineConsoleView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is EngineConsoleViewModel viewModel)
                viewModel.Lines.CollectionChanged += OnLinesChanged;
        };
    }

    private void OnLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
            LogScrollViewer.ScrollToEnd();
    }
}
