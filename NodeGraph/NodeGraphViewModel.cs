using System.Collections.ObjectModel;
using Avalonia.Threading;
using Toybox.Studio.EngineApi;
using Toybox.Studio.EngineApi.Types.Worlds;
using Toybox.Studio.Utils;
using Toybox.Studio.Utils.Composition;
using Toybox.Studio.WorldTree;

namespace Toybox.Studio.NodeGraph;

/// <summary>
/// One viewport pane's node overlay: a node per entity in the active world, drawn over the live render and
/// positioned each frame from the engine's projection (<see cref="ViewportStream.ProjectEntitiesAsync"/>) —
/// so nodes track their entities and stay a near-constant on-screen size as the camera moves. Nodes whose
/// entities project close together are grouped into a <em>deck</em>: stacked like cards, and fanned out
/// while the pointer is over any of them. It owns the <see cref="Nodes"/>, the speech-bubble <see cref="Tails"/>
/// (node → entity), and the reference <see cref="Links"/> (node → referenced entity's node). The set is
/// rebuilt only when the entity set changes; a per-frame tick re-places the existing nodes and re-clusters
/// the decks. Node placement/collapse persists per world through the <see cref="NodeLayoutManager"/>.
/// </summary>
public sealed class NodeGraphViewModel : IDisposable
{
    // ~30 Hz: fast enough to track a moving camera, cheap enough to project a whole world each tick.
    private static readonly TimeSpan Cadence = TimeSpan.FromMilliseconds(33);
    // Idle cadence: when the projection stops changing (camera parked, nothing edited), the overlay only
    // needs the occasional refresh, so back off to ~5 Hz to stop projecting (and JSON-serializing) the
    // whole world 30 times a second per pane. Any change snaps straight back to the fast cadence, so a
    // parked overlay costs little while a moving one stays smooth — a missed change lags at most one idle
    // tick (~200 ms), never freezes.
    private static readonly TimeSpan IdleCadence = TimeSpan.FromMilliseconds(200);
    // Consecutive unchanged projections before dropping to the idle cadence (~0.5 s at the fast rate).
    private const int StaticTicksToIdle = 15;

    // Distance-scale tuning (Dreams-style near-billboard): nodes hold close to a constant on-screen size
    // with only gentle distance falloff, so they stay big and legible. scale = ReferenceDepth / depth,
    // clamped to [MinScale, MaxScale]; the high floor is what keeps distant nodes readable.
    private const double ReferenceDepth = 30.0;
    private const double MinScale = 0.8;
    private const double MaxScale = 1.0;

    // Deck layout: nodes whose base centers fall in the same CellSize grid cell are one deck; a stacked
    // deck cascades by StackStep, a fanned (hovered) deck spreads by FanStep. Both scale with the node.
    private const double CellSize = 130.0;
    private const double StackStep = 11.0;
    private const double FanStep = 84.0;

    private readonly ViewportStream _stream;
    private readonly Engine _engine;
    private readonly GameState _game;
    private readonly WorldSelection _selection;
    private readonly ViewModelFactory _viewModels;
    private readonly NodeLayoutManager _layouts;

    private readonly Dictionary<ulong, EntityNodeViewModel> _nodes = [];
    private readonly Dictionary<ulong, TailViewModel> _tails = [];
    private readonly List<(ulong Source, ulong Target, OverlayEdgeViewModel Edge)> _links = [];

    // Per-frame scratch: each visible node's entity screen point (tail apex) and camera distance (deck order).
    private readonly Dictionary<ulong, (double ApexX, double ApexY, double Depth)> _frame = [];
    private readonly Dictionary<(int, int), List<EntityNodeViewModel>> _decks = [];

    private readonly DispatcherTimer _timer;
    private World? _world;
    private IReadOnlyList<Entity> _builtEntities = [];
    private double _viewWidth;
    private double _viewHeight;
    private bool _projecting;
    private bool _disposed;
    // Adaptive-cadence state: a hash of the last projection's screen positions and how many consecutive
    // ticks it has stayed unchanged, used to fall back to IdleCadence while nothing moves.
    private int _projectionHash;
    private int _staticTicks;
    private bool _idle;

    public NodeGraphViewModel(
        ViewportStream stream, Engine engine, GameState game, WorldSelection selection,
        ViewModelFactory viewModels, NodeLayoutManager layouts)
    {
        _stream = stream;
        _engine = engine;
        _game = game;
        _selection = selection;
        _viewModels = viewModels;
        _layouts = layouts;

        _game.ActiveChanged += OnActiveChanged;
        _selection.Changed += OnSelectionChanged;
        _timer = new DispatcherTimer(Cadence, DispatcherPriority.Background, OnTick);
        OnActiveChanged();
        _timer.Start();
    }

    /// <summary>The entity nodes, rendered on the overlay canvas (positioned by this view-model).</summary>
    public ObservableCollection<EntityNodeViewModel> Nodes { get; } = [];

    /// <summary>The speech-bubble tails from each node to its entity (drawn beneath the nodes).</summary>
    public ObservableCollection<TailViewModel> Tails { get; } = [];

    /// <summary>The reference links between nodes (drawn beneath the nodes).</summary>
    public ObservableCollection<OverlayEdgeViewModel> Links { get; } = [];

    /// <summary>The view reports its pixel size here (the overlay's coordinate space is the surface's image
    /// rect); positions re-derive against it.</summary>
    public void SetViewSize(double width, double height)
    {
        _viewWidth = width;
        _viewHeight = height;
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Stop();
        _game.ActiveChanged -= OnActiveChanged;
        _selection.Changed -= OnSelectionChanged;
        if (_world is not null)
            _world.Changed -= OnWorldChanged;
        ClearNodes();
    }

    private void OnActiveChanged() => Dispatch.To(DispatchContext.UI, () =>
    {
        if (_world is not null)
            _world.Changed -= OnWorldChanged;
        _world = _game.Active;
        if (_world is not null)
            _world.Changed += OnWorldChanged;
        RebuildNodes();
    });

    // Only a structural change (an entity added/removed) replaces the registry list; a value edit keeps it,
    // so a rename/gizmo drag doesn't tear down the overlay.
    private void OnWorldChanged() => Dispatch.To(DispatchContext.UI, () =>
    {
        if (_world is { } world && !ReferenceEquals(world.Entities, _builtEntities))
            RebuildNodes();
    });

    private void RebuildNodes()
    {
        ClearNodes();
        if (_world is not { } world)
            return;

        _builtEntities = world.Entities;
        var worldId = world.WorldId;

        foreach (var entity in world.Entities)
        {
            var node = new EntityNodeViewModel(
                entity, _viewModels, _layouts.Layout(worldId, entity.Id), () => _layouts.Save(worldId),
                () => _selection.Set(entity.Id));
            _nodes[entity.Id] = node;
            Nodes.Add(node);

            var tail = new TailViewModel { IsVisible = false };
            _tails[entity.Id] = tail;
            Tails.Add(tail);
        }

        // Plugs and links need every node built first. Every entity is referenceable, so every node carries an
        // output plug; a node that holds an entity reference carries an input plug. Each entity reference wires
        // the referenced entity's output → the holder's input (provider → consumer).
        foreach (var node in _nodes.Values)
        {
            node.HasOutput = true;
            node.HasInput = node.References.Count > 0;

            foreach (var reference in node.References)
                if (reference.Kind == ReferenceKind.Entity && _nodes.ContainsKey(reference.TargetEntityId))
                {
                    var edge = new OverlayEdgeViewModel { IsVisible = false };
                    _links.Add((reference.TargetEntityId, node.EntityId, edge));
                    Links.Add(edge);
                }
        }

        ApplyFocus();
    }

    // The active selection changed: re-derive each node's state (the selected open, their linked neighbours as
    // plugs, the rest hidden).
    private void OnSelectionChanged() => Dispatch.To(DispatchContext.UI, ApplyFocus);

    // Selected entities' nodes open; any node linked to a selected one (either direction) becomes a plug; every
    // other node hides. A plug click selects it, so the focus walks the reference graph.
    private void ApplyFocus()
    {
        var selected = new HashSet<ulong>(_selection.SelectedIds);
        var connected = new HashSet<ulong>();
        foreach (var (source, target, _) in _links)
        {
            if (selected.Contains(source))
                connected.Add(target);
            if (selected.Contains(target))
                connected.Add(source);
        }

        foreach (var (id, node) in _nodes)
            node.State = selected.Contains(id) ? NodeVisualState.Open
                : connected.Contains(id) ? NodeVisualState.Plug
                : NodeVisualState.Hidden;
    }

    private void ClearNodes()
    {
        foreach (var node in _nodes.Values)
            node.Dispose();
        _nodes.Clear();
        _tails.Clear();
        _links.Clear();
        _frame.Clear();
        _decks.Clear();
        Nodes.Clear();
        Tails.Clear();
        Links.Clear();
        _builtEntities = [];
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_projecting || _disposed || _nodes.Count == 0 || _viewWidth <= 0 || _viewHeight <= 0)
            return;
        if (!_engine.IsConnected || _stream.ViewName is null)
            return;

        _projecting = true;
        ProjectAsync().FireAndForget();
    }

    private async Task ProjectAsync()
    {
        try
        {
            var result = await _stream.ProjectEntitiesAsync().ContinueOnSameContext();
            if (_disposed || !result || result.Value is not { } screens)
                return;

            UpdateCadence(screens);
            Apply(screens);
        }
        finally
        {
            _projecting = false;
        }
    }

    // Slows the tick to IdleCadence once the projection has been identical for StaticTicksToIdle ticks
    // (nothing moving), and snaps back to the fast Cadence the instant it changes again. The hash is
    // order-independent (XOR of per-entity position hashes) and rounded so sub-pixel float noise doesn't
    // read as motion. Cost is O(entities) — far cheaper than the RPC round-trip it decides to skip.
    private void UpdateCadence(IReadOnlyList<EntityScreen> screens)
    {
        var hash = 0;
        foreach (var screen in screens)
            hash ^= HashCode.Combine(
                screen.Id,
                Math.Round(screen.U, 3),
                Math.Round(screen.V, 3),
                Math.Round(screen.Depth, 2));

        if (hash == _projectionHash)
        {
            if (_staticTicks < StaticTicksToIdle && ++_staticTicks == StaticTicksToIdle)
            {
                _idle = true;
                _timer.Interval = IdleCadence;
            }
            return;
        }

        _projectionHash = hash;
        _staticTicks = 0;
        if (_idle)
        {
            _idle = false;
            _timer.Interval = Cadence;
        }
    }

    // Re-places every node from the latest projection, groups overlapping nodes into decks, then updates the
    // tail and link geometry. Runs on the UI thread (the tick and its awaited continuation marshal there).
    private void Apply(IReadOnlyList<EntityScreen> screens)
    {
        // Pass 1 — base placement. Record each visible node's apex + depth for the deck/tail passes.
        _frame.Clear();
        foreach (var screen in screens)
        {
            if (!_nodes.TryGetValue(screen.Id, out var node))
                continue;

            var apexX = screen.U * _viewWidth;
            var apexY = screen.V * _viewHeight;
            var scale = Math.Clamp(ReferenceDepth / Math.Max(screen.Depth, 0.001), MinScale, MaxScale);
            node.PrePlace(apexX, apexY, scale);
            node.IsNodeVisible = true;
            _frame[screen.Id] = (apexX, apexY, screen.Depth);
        }

        foreach (var (id, node) in _nodes)
            if (!_frame.ContainsKey(id))
            {
                node.IsNodeVisible = false;
                if (_tails.TryGetValue(id, out var hiddenTail))
                    hiddenTail.IsVisible = false;
            }

        LayoutDecks();
        UpdateTails();
        UpdateLinks();
    }

    // Groups visible nodes by their base-center grid cell, then lays each group out as a stacked or (while
    // hovered) fanned deck, front-to-back by camera distance.
    private void LayoutDecks()
    {
        _decks.Clear();
        foreach (var id in _frame.Keys)
        {
            var node = _nodes[id];

            // A hidden node takes no deck slot (it would fan invisible cards and spread the visible ones); it
            // just sits at its base position, invisible.
            if (!node.IsShown)
            {
                node.Commit(0, 0, 0);
                continue;
            }

            var (cx, cy) = node.BaseCenter;
            var cell = ((int)Math.Floor(cx / CellSize), (int)Math.Floor(cy / CellSize));
            if (!_decks.TryGetValue(cell, out var members))
                _decks[cell] = members = [];
            members.Add(node);
        }

        foreach (var members in _decks.Values)
        {
            if (members.Count == 1)
            {
                members[0].Commit(0, 0, 100);
                continue;
            }

            // Nearest first (drawn on top); a deck fans while the pointer is over any of its cards.
            members.Sort((a, b) => _frame[a.EntityId].Depth.CompareTo(_frame[b.EntityId].Depth));
            var fanned = members.Any(member => member.IsHovered);
            var scale = members[0].Scale;

            for (var i = 0; i < members.Count; i++)
            {
                if (fanned)
                {
                    var offset = (i - ((members.Count - 1) / 2.0)) * FanStep * scale;
                    members[i].Commit(offset, 0, 1000 + members.Count - i);
                }
                else
                {
                    members[i].Commit(i * StackStep * scale, i * StackStep * scale, 500 + members.Count - i);
                }
            }
        }
    }

    private void UpdateTails()
    {
        foreach (var (id, frame) in _frame)
        {
            if (!_tails.TryGetValue(id, out var tail))
                continue;

            var node = _nodes[id];
            if (!node.IsShown)
            {
                tail.IsVisible = false;
                continue;
            }

            // The node's overlay rect is its un-scaled box times the distance-scale, at its committed top-left.
            tail.Update(
                node.X, node.Y, node.CardWidth * node.Scale, node.CardHeight * node.Scale,
                node.TailHalfBase, frame.ApexX, frame.ApexY);
            tail.IsVisible = true;
        }
    }

    private void UpdateLinks()
    {
        foreach (var (source, target, edge) in _links)
        {
            if (_nodes.TryGetValue(source, out var from) && _nodes.TryGetValue(target, out var to)
                && from is { IsNodeVisible: true, IsShown: true }
                && to is { IsNodeVisible: true, IsShown: true })
            {
                // Source's output plug (right edge) → target's input plug (left edge).
                edge.Update(from.OutputPoint, to.InputPoint);
                edge.IsVisible = true;
            }
            else
            {
                edge.IsVisible = false;
            }
        }
    }
}
