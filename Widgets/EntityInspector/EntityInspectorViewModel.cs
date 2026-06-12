using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json.Linq;
using Toybox.Studio.Services;

namespace Toybox.Studio.Widgets.EntityInspector;

public sealed partial class EntityInspectorViewModel : ObservableObject
{
    private readonly EngineSession _session;
    private readonly WorldManager _scene;
    private readonly WorldSelection _selection;

    public EntityInspectorViewModel(
        WorldSelection selection,
        EngineSession session,
        WorldManager scene)
    {
        _selection = selection;
        _session = session;
        _scene = scene;
        selection.SelectionChanged += node => Dispatch.To(DispatchContext.UI, () => ApplySelection(node));
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    public partial WorldNode? Entity { get; private set; }

    [ObservableProperty]
    public partial string Subtitle { get; private set; } = "";

    public bool HasSelection => Entity is not null;

    /// <summary>
    /// The selected entity's components, each as a type-driven property grid group.
    /// </summary>
    public ObservableCollection<ComponentGroupViewModel> Components { get; } = [];

    private void ApplySelection(WorldNode? node)
    {
        Entity = node;
        Subtitle = node is null
            ? ""
            : string.IsNullOrEmpty(node.Tag) ? $"id {node.Id}" : $"id {node.Id} — tag '{node.Tag}'";

        Components.Clear();
        if (node is null)
            return;

        var entityId = node.Id;
        foreach (var component in node.Components)
        {
            var raw = component.Raw;
            var name = component.Name;
            Components.Add(new ComponentGroupViewModel(
                component,
                commit: () => OnComponentEdited(entityId, name, raw)));
        }
    }

    /// <summary>
    /// A property leaf mutated the component's JSON in place; push the whole component back.
    /// </summary>
    private async void OnComponentEdited(ulong entityId, string component, JObject raw)
    {
        var client = _session.Client;
        if (client is not { IsConnected: true })
            return;

        try
        {
            // Stay on the UI context: the catch shows a dialog and re-drives selection, both UI work.
            await client.SetComponentAsync(entityId, component, raw, CancellationToken.None)
                .ContinueOnSameContext();
        }
        catch (Exception exception)
        {
            await Dialogs.ShowErrorAsync(
                "Couldn't apply change",
                $"The engine rejected the edit to '{component}':\n\n{exception.Message}")
                .ContinueOnSameContext();

            // Revert the inspector to the engine's truth by re-reading the scene and reselecting.
            await _scene.RefreshAsync().ContinueOnSameContext();
            if (_scene.Find(entityId) is { } refreshed)
                _selection.Select(refreshed);
        }
    }
}
