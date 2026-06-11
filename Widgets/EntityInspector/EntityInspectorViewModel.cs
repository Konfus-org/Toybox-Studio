using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Toybox.Studio.Services;

namespace Toybox.Studio.Widgets.EntityInspector;

public sealed partial class EntityInspectorViewModel : ObservableObject
{
    public EntityInspectorViewModel(SceneSelectionService selection)
    {
        selection.SelectionChanged += node => Dispatcher.UIThread.Post(() => ApplySelection(node));
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    public partial SceneNode? Entity { get; private set; }

    [ObservableProperty]
    public partial string Subtitle { get; private set; } = "";

    public bool HasSelection => Entity is not null;

    private void ApplySelection(SceneNode? node)
    {
        Entity = node;
        Subtitle = node is null
            ? ""
            : string.IsNullOrEmpty(node.Tag) ? $"id {node.Id}" : $"id {node.Id} — tag '{node.Tag}'";
    }
}
