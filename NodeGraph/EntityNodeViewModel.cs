using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using Toybox.Studio.EngineApi.Types.Components;
using Toybox.Studio.EngineApi.Types.Worlds;
using Toybox.Studio.PropertyGrid;
using Toybox.Studio.Utils.Composition;
using Icon = IconPacks.Avalonia.Lucide.PackIconLucideKind;

namespace Toybox.Studio.NodeGraph;

/// <summary>
/// One entity's node over the world view. Its visual <see cref="State"/> is driven by the selection: the
/// selected entity's node is <see cref="NodeVisualState.Open"/> — a tabbed inspector (Components / Scripts) —
/// while an entity referenced by (or referencing) the selection surfaces as a <see cref="NodeVisualState.Plug"/>:
/// a small connector square that fades in and, when clicked, selects that entity (opening it and walking the
/// focus). Everything else is <see cref="NodeVisualState.Hidden"/>. Its overlay-pixel position
/// (<see cref="X"/>/<see cref="Y"/>, bound to the canvas), distance-scale, deck offset, and z-order are all
/// driven by the <see cref="NodeGraphViewModel"/> from the engine's per-frame projection. The tab content is
/// built lazily on open and dropped otherwise, which is what keeps hundreds of nodes affordable.
/// </summary>
public sealed partial class EntityNodeViewModel : ObservableObject, IDisposable
{
    // The node's un-scaled footprint; the visual distance-scale renders on top via a transform, so these stay
    // constant per state. A connected-but-unselected entity shows as the small Plug square; the selected one
    // opens to the full inspector card (whose header Border is 46 tall in EntityNodeView.axaml).
    private const double PlugWidth = 64;
    private const double PlugHeight = 64;
    private const double ExpandedWidth = 440;
    private const double ExpandedHeight = 380;

    // The un-scaled gap the card floats above its entity, leaving room for the tail.
    private const double TailGap = 34;

    private readonly Entity _entity;
    private readonly ViewModelFactory _viewModels;
    private readonly NodeLayout _layout;
    private readonly Action _persist;
    private readonly Action _activate;

    // The card's projected top-left before the deck offset — recomputed each frame; the deck layout offsets
    // the final X/Y from it.
    private double _baseX;
    private double _baseY;

    // Suppresses the collapse side effects (rebuild/persist) while the constructor seeds the initial state.
    private bool _ready;

    public EntityNodeViewModel(
        Entity entity, ViewModelFactory viewModels, NodeLayout layout, Action persist, Action activate)
    {
        _entity = entity;
        _viewModels = viewModels;
        _layout = layout;
        _persist = persist;
        _activate = activate;

        Name = entity.Name;
        Icon = NodeIcons.ForEntity(entity);
        References = ReferenceScanner.EntityReferences(entity);

        _entity.Changed += OnEntityChanged;
        _ready = true;
    }

    public ulong EntityId => _entity.Id;

    public Entity Entity => _entity;

    /// <summary>The entity's type icon (from its most characterizing component).</summary>
    public Icon Icon { get; }

    /// <summary>The entity's references (entity references drive the links between nodes).</summary>
    public IReadOnlyList<NodeReference> References { get; }

    /// <summary>The entity's display name, kept live off the entity's own change signal.</summary>
    [ObservableProperty]
    public partial string Name { get; private set; }

    /// <summary>The node's selection-driven presentation (hidden / plug / open) — set by the graph from the
    /// shared selection, not persisted. Open builds the tabbed inspector; plug fades in a connector square.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CardWidth), nameof(CardHeight), nameof(IsOpen), nameof(IsPlug),
        nameof(IsShown), nameof(Appearance))]
    public partial NodeVisualState State { get; set; }

    /// <summary>Whether the node is the open inspector card (its entity is selected).</summary>
    public bool IsOpen => State == NodeVisualState.Open;

    /// <summary>Whether the node is a connector plug (its entity is connected to the selection).</summary>
    public bool IsPlug => State == NodeVisualState.Plug;

    /// <summary>Whether the node shows at all (open or plug).</summary>
    public bool IsShown => State != NodeVisualState.Hidden;

    /// <summary>The node's target opacity — 1 when shown, 0 when hidden. A view-side transition on this fades a
    /// plug in as it becomes connected to the selection.</summary>
    public double Appearance => IsShown ? 1.0 : 0.0;

    /// <summary>The Components tab — one row per non-script component (built lazily on expand).</summary>
    [ObservableProperty]
    public partial IReadOnlyList<ComponentCardViewModel> Components { get; private set; } = [];

    /// <summary>The Scripts tab — the entity's script container grid, or null when it has no scripts.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasScripts))]
    public partial PropertyGridViewModel? Scripts { get; private set; }

    /// <summary>Whether the entity has a script container (gates the Scripts tab's content vs a placeholder).</summary>
    public bool HasScripts => Scripts is not null;

    /// <summary>The distance-scale the card renders at (a <c>ScaleTransform</c> in the view, from the
    /// top-left); the card's layout box stays at the un-scaled <see cref="CardWidth"/>×<see cref="CardHeight"/>
    /// so the placement and tail/link endpoints stay stable.</summary>
    [ObservableProperty]
    public partial double Scale { get; private set; } = 1;

    /// <summary>The node's top-left in overlay pixels, bound to <c>Canvas.Left</c>.</summary>
    [ObservableProperty]
    public partial double X { get; private set; }

    /// <summary>The node's top in overlay pixels, bound to <c>Canvas.Top</c>.</summary>
    [ObservableProperty]
    public partial double Y { get; private set; }

    /// <summary>The container's z-order (nearer / hovered nodes draw on top).</summary>
    [ObservableProperty]
    public partial int ZIndex { get; private set; }

    /// <summary>Whether the pointer is over this card (set by a hover behavior) — the graph fans the card's
    /// deck while any member is hovered.</summary>
    [ObservableProperty]
    public partial bool IsHovered { get; set; }

    /// <summary>Whether the node is on screen (hidden when its entity is behind the camera).</summary>
    [ObservableProperty]
    public partial bool IsNodeVisible { get; set; }

    /// <summary>Whether another entity references this one (so it shows an input plug on its left edge).</summary>
    [ObservableProperty]
    public partial bool HasInput { get; set; }

    /// <summary>Whether this entity holds any reference (so it shows an output plug on its right edge).</summary>
    [ObservableProperty]
    public partial bool HasOutput { get; set; }

    /// <summary>The node's un-scaled width (the small plug square, or the open inspector card).</summary>
    public double CardWidth => IsOpen ? ExpandedWidth : PlugWidth;

    /// <summary>The node's un-scaled height (the small plug square, or the open inspector card).</summary>
    public double CardHeight => IsOpen ? ExpandedHeight : PlugHeight;

    /// <summary>Computes the card's base position (before the deck offset) from its entity's projected screen
    /// point, distance-scaled, plus the user's saved drag offset. Called each frame before deck layout.</summary>
    public void PrePlace(double entityX, double entityY, double scale)
    {
        Scale = scale;
        // The card renders scaled from its top-left (RenderTransformOrigin 0,0), so its visual footprint is
        // CardWidth/Height × scale. Sit its bottom-center a scaled gap above the entity's screen point.
        _baseX = entityX - (CardWidth * scale / 2) + _layout.OffsetX;
        _baseY = entityY - (TailGap * scale) - (CardHeight * scale) + _layout.OffsetY;
    }

    /// <summary>The card's base center in overlay pixels — what decks are clustered by.</summary>
    public (double X, double Y) BaseCenter =>
        (_baseX + (CardWidth * Scale / 2), _baseY + (CardHeight * Scale / 2));

    /// <summary>Commits the final position for this frame: the base position plus the deck layout offset,
    /// at the given z-order.</summary>
    public void Commit(double deckOffsetX, double deckOffsetY, int zIndex)
    {
        X = _baseX + deckOffsetX;
        Y = _baseY + deckOffsetY;
        ZIndex = zIndex;
    }

    /// <summary>The input plug's point — the left-edge middle, in overlay pixels (a link's target end).</summary>
    public Point InputPoint => new(X, Y + (CardHeight * Scale / 2));

    /// <summary>The output plug's point — the right-edge middle, in overlay pixels (a link's source end).</summary>
    public Point OutputPoint => new(X + (CardWidth * Scale), Y + (CardHeight * Scale / 2));

    /// <summary>Half the width of the tail's base on the node edge, in overlay pixels (distance-scaled).</summary>
    public double TailHalfBase => 11 * Scale;

    /// <summary>Records a user drag by shifting the saved offset (the next frame re-places from it), then
    /// persists it.</summary>
    public void Drag(double deltaX, double deltaY)
    {
        _layout.OffsetX += deltaX;
        _layout.OffsetY += deltaY;
        X += deltaX;
        Y += deltaY;
        _persist();
    }

    /// <summary>The node's click action: selects this entity (a plug click opens it and walks the focus; on an
    /// already-open node it is a no-op). The graph wires this to the shared selection.</summary>
    public void Activate() => _activate();

    partial void OnStateChanged(NodeVisualState value)
    {
        if (!_ready)
            return;

        // The heavy inspector lives only while open; a plug/hidden node carries none.
        if (value == NodeVisualState.Open)
            BuildContent();
        else
            ClearContent();
    }

    private void BuildContent()
    {
        Components = _entity.Components
            .Where(component => component is not ScriptContainer)
            .Select(component => new ComponentCardViewModel(component, _viewModels))
            .ToList();

        if (_entity.Components.OfType<ScriptContainer>().FirstOrDefault() is { } scripts)
        {
            var grid = _viewModels.Create<PropertyGridViewModel>(new ReflectionPropertyNodeFactory(_viewModels));
            grid.Show(scripts);
            Scripts = grid;
        }
        else
        {
            Scripts = null;
        }
    }

    private void ClearContent()
    {
        Components = [];
        Scripts = null;
    }

    private void OnEntityChanged() => Name = _entity.Name;

    public void Dispose() => _entity.Changed -= OnEntityChanged;
}
