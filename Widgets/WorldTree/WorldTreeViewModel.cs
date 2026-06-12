using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toybox.Studio.Services;

namespace Toybox.Studio.Widgets.WorldTree;

public sealed partial class WorldTreeViewModel : ObservableObject
{
    private readonly WorldManager _scene;
    private readonly WorldSelection _selection;

    public WorldTreeViewModel(WorldManager scene, WorldSelection selection)
    {
        _scene = scene;
        _selection = selection;
        scene.SceneUpdated += roots => Dispatch.To(DispatchContext.UI, () => ApplyScene(roots));
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasScene))]
    public partial IReadOnlyList<WorldNode> RootNodes { get; private set; } = [];

    public bool HasScene => RootNodes.Count > 0;

    [ObservableProperty]
    public partial WorldNode? SelectedNode { get; set; }

    [ObservableProperty]
    public partial string Summary { get; private set; } = "";

    [RelayCommand]
    private Task RefreshAsync()
    {
        return _scene.RefreshAsync();
    }

    partial void OnSelectedNodeChanged(WorldNode? value)
    {
        _selection.Select(value);
    }

    private void ApplyScene(IReadOnlyList<WorldNode> roots)
    {
        RootNodes = roots;
        SelectedNode = null;
        var total = CountRecursively(roots);
        Summary = total == 0 ? "" : $"{total} entities";
    }

    private static int CountRecursively(IReadOnlyList<WorldNode> nodes)
    {
        var count = nodes.Count;
        foreach (var node in nodes)
            count += CountRecursively(node.Children);
        return count;
    }
}
