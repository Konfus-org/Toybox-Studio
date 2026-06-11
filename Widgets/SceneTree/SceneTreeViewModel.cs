using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toybox.Studio.Services;

namespace Toybox.Studio.Widgets.SceneTree;

public sealed partial class SceneTreeViewModel : ObservableObject
{
    private readonly EngineSceneService _scene;
    private readonly SceneSelectionService _selection;

    public SceneTreeViewModel(EngineSceneService scene, SceneSelectionService selection)
    {
        _scene = scene;
        _selection = selection;
        scene.SceneUpdated += roots => Dispatcher.UIThread.Post(() => ApplyScene(roots));
    }

    [ObservableProperty]
    public partial IReadOnlyList<SceneNode> RootNodes { get; private set; } = [];

    [ObservableProperty]
    public partial SceneNode? SelectedNode { get; set; }

    [ObservableProperty]
    public partial string Summary { get; private set; } = "No scene";

    [RelayCommand]
    private Task RefreshAsync()
    {
        return _scene.RefreshAsync();
    }

    partial void OnSelectedNodeChanged(SceneNode? value)
    {
        _selection.Select(value);
    }

    private void ApplyScene(IReadOnlyList<SceneNode> roots)
    {
        RootNodes = roots;
        SelectedNode = null;
        var total = CountRecursively(roots);
        Summary = total == 0 ? "No scene" : $"{total} entities";
    }

    private static int CountRecursively(IReadOnlyList<SceneNode> nodes)
    {
        var count = nodes.Count;
        foreach (var node in nodes)
            count += CountRecursively(node.Children);
        return count;
    }
}
