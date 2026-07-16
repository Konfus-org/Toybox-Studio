using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json.Linq;
using Toybox.Studio.AssetOwners;
using Toybox.Studio.EngineApi.Types.Worlds;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Events;
using Toybox.Studio.Hosting;
using Toybox.Studio.Keybindings;
using Toybox.Studio.Logging;
using Toybox.Studio.NodeGraph;
using Toybox.Studio.Splitting;
using Toybox.Studio.Toolbar;
using Toybox.Studio.Utils;
using Toybox.Studio.Utils.Composition;
using Toybox.Studio.Viewport;
using Toybox.Studio.WorldTree;

namespace Toybox.Studio.WorldViewer;

/// <summary>
/// The World Viewer dockable's view-model — a proper asset owner over the active editing
/// <see cref="World"/>. It embeds the reusable viewport split grid (the panel's whole body), minting each
/// pane itself — a fully-wired <see cref="ViewKind.Editor"/> viewport with its transform + render-layer
/// toolbars, click-select pick handler, and its own engine-camera stream — and, once the engine connects,
/// loads the active world as a bound <see cref="World"/> mirror it hosts. From there the panel is an
/// <see cref="AssetOwnerViewModel"/> like any other: its tab carries the world's dirty star, and
/// File▸Save / Ctrl+S / Undo / Redo act on the world. World edits push live per entity/component; the
/// sync base folds every one of them into the world's <see cref="Asset.IsDirty"/> flag, and Save persists
/// them through <c>asset.save</c> (which the engine routes to the world's chunk + globals files). Its own
/// type is the panel identity and the input scheme scope; it stays the split host (delegating to the
/// embedded grid) so the persisted split layout still round-trips.
/// </summary>
public sealed class WorldViewerViewModel : AssetOwnerViewModel,
    ISplitHost, IEventHandler<ConnectionChanged>, IEventHandler<EditTransactionChanged>, IDisposable
{
    private readonly Engine _engine;
    private readonly SyncHub _hub;
    private readonly EventDispatcher _events;
    private readonly Logger _log;
    private readonly ViewModelFactory _viewModels;
    private readonly WorldPickHandler _pick;
    private readonly GameState _game;

    private World? _world;

    public WorldViewerViewModel(
        Engine engine, SyncHub hub, EventDispatcher events, Logger log,
        ViewModelFactory viewModels, WorldPickHandler pick, GameState game)
    {
        _engine = engine;
        _hub = hub;
        _events = events;
        _log = log;
        _viewModels = viewModels;
        _pick = pick;
        _game = game;
        Split = new ViewportSplitViewModel(CreatePane);

        events.RegisterAll(this);
        if (engine.IsConnected)
            LoadWorldAsync().FireAndForget();
    }

    // Mints one fully-wired editor pane for the split grid: a viewport with its own transform + render-layer
    // toolbars, bound to a freshly started editor-camera stream. The grid calls this once per pane and owns
    // the returned view-model's lifetime (disposing it stops the stream); each call is independent so joining
    // a pane disposes only its own toolbars. The pick handler is shared — it only writes the one global
    // selection.
    private ViewportViewModel CreatePane()
    {
        // The pane's engine-camera stream is created first so both the viewport (which presents its texture)
        // and the node overlay (which polls it for per-frame entity projection) share the one view.
        var stream = new ViewportStream(_engine, _events, ViewKind.Editor);

        // The two toolbars and the node overlay resolve through the factory; the viewport takes them plus
        // this editor's shared pick handler as runtime arguments (the factory injects its services).
        var overlay = _viewModels.Create<NodeGraphViewModel>(stream);
        var toolbars = new ToolbarViewModel[]
        {
            _viewModels.Create<TransformToolbarViewModel>(),
            _viewModels.Create<RenderLayersToolbarViewModel>(),
        };
        var viewport = _viewModels.Create<ViewportViewModel>(ViewKind.Editor, toolbars, _pick, overlay);
        viewport.Prepare(stream);
        return viewport;
    }

    /// <summary>The embedded viewport split grid — the panel's whole body.</summary>
    public ViewportSplitViewModel Split { get; }

    protected override string DisplayName => _world?.Name is { Length: > 0 } name ? name : "World";

    /// <summary>The owned body is the viewport grid; the view renders it directly (no buffered Save/Cancel
    /// chrome — a world's edits are live, saved through the tab's dirty star and File▸Save).</summary>
    public override object? Body => Split;

    public void Handle(in ConnectionChanged evt)
    {
        // The engine's active world is process state; (re)load it whenever the connection comes up.
        if (evt.State == ConnectionState.Connected)
            Dispatch.To(DispatchContext.UI, () => LoadWorldAsync().FireAndForget());
    }

    // The engine brackets an interactive edit (a gizmo drag) with a begin/commit pair around the burst of
    // transform changes it streams. Turn that into one undo step on the hosted world. Marshalled to the UI
    // thread so it sequences after those changes' own applies (both arrive as ordered engine notifications
    // and post to the UI dispatcher in order), leaving the committed snapshot complete.
    public void Handle(in EditTransactionChanged evt)
    {
        var phase = evt.Phase;
        Dispatch.To(DispatchContext.UI, () =>
        {
            if (phase == EditTransactionPhase.Begin)
                BeginEditTransaction();
            else
                EndEditTransaction();
        });
    }

    void ISplitHost.BindSplitLayout(SplitLayout layout) => Split.BindSplitLayout(layout);

    public void Dispose()
    {
        _events.UnregisterAll(this);
        if (ReferenceEquals(_game.Active, _world))
            _game.SetActive(null);
        _world?.Dispose();
        Split.Dispose();
    }

    // Reads the active world (its id plus every entity, with components) from the engine and hosts it as a
    // bound World mirror. A reconnect rebuilds it, dropping the previous mirror graph.
    private async Task LoadWorldAsync()
    {
        var reply = await _engine
            .SendCommandAsync<JObject>(EngineCommands.WorldDescribe, new JObject())
            .ContinueOnSameContext();
        if (!reply)
        {
            _log.Warning($"The World Viewer couldn't load the active world: {reply.Error}");
            return;
        }

        _world?.Dispose();
        var world = new World();
        world.Deserialize(reply.Value!);  // id + entities (+ their components), applied locally
        world.Bind(_hub);                 // the world at asset/{id}; its entities and components bind as children
        _world = world;
        Host(world);                      // dirty star, Save, and Undo/Redo — all from the owner base
        _game.SetActive(world);           // publish as the active editing world (the Hierarchy panel reads it)
        OnPropertyChanged(nameof(Title));
    }
}
