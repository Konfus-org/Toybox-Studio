using Avalonia.Controls.Documents;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using Avalonia;
using System.Collections.Specialized;
using System.Collections;
using System.Linq;
using System.Windows.Input;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Console;

/// <summary>
/// The console's scrollback as a single selectable text stream (not a per-line list), so a drag selects
/// across many lines and one copy takes them all. It renders each <see cref="ConsoleLine"/> as coloured
/// runs — turning a line's <see cref="ConsoleLink"/> span into a clickable, underlined link — and owns the
/// tailing/selection/scroll behaviour that keeps a live log readable:
/// <list type="bullet">
/// <item>tails new output while parked at the bottom, but stops the moment there's a selection (so a read
/// isn't yanked away) or the user scrolls up;</item>
/// <item>resumes tailing when the view returns to the bottom, or when it loses focus (which also clears the
/// selection);</item>
/// <item>a press anywhere that isn't the text — the scrollbar, the empty gutter — clears the selection,
/// without fighting a scrollbar drag (tailing is governed by scroll position, never forced mid-drag).</item>
/// </list>
/// It registers itself as the <see cref="ConsoleViewModel"/>'s <see cref="IConsoleTextInteraction"/> while
/// attached, so a right-click menu routed at the view-model can copy/select the live text.
/// </summary>
public sealed class ConsoleTextView : SelectableTextBlock, IConsoleTextInteraction
{
    // How close (px) to the bottom still counts as "parked at the tail".
    private const double BottomThreshold = 2.0;

    // How far (px) the pointer may drift between press and release and still count as a click, not a
    // drag-selection. A click that drifts even one glyph leaves the base SelectableTextBlock with a
    // one-character selection, so this tolerance — not "is there any selection?" — decides link clicks.
    private const double DragThreshold = 3.0;

    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);
    private static readonly Cursor BeamCursor = new(StandardCursorType.Ibeam);

    // Per-rendered-line bookkeeping, parallel to Inlines, so appends/trims stay incremental: the flat text
    // length this line contributes (its text plus a trailing newline), how many inlines it owns, and its
    // link span (relative to the line's text) if any.
    private readonly List<LineSpan> _lines = [];

    private ScrollViewer? _scroll;
    private INotifyCollectionChanged? _subscribedItems;
    private ConsoleViewModel? _registeredVm;

    // While true, new output scrolls into view. Cleared by scrolling up, restored at the bottom or on focus
    // loss. Starts true so the console tails by default.
    private bool _stick = true;
    private bool _cursorIsHand;

    // The pointer position at the last press and its click count, so a release can tell a click (barely
    // moved, single click) from a drag-selection or a double-click word-select.
    private Point? _pressPoint;
    private int _pressClickCount;

    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<ConsoleTextView, IEnumerable?>(nameof(ItemsSource));

    /// <summary>Invoked with the clicked <see cref="ConsoleLink"/> (its <see cref="ConsoleLink.Target"/> plus
    /// any <see cref="ConsoleLink.Line"/>) when a link is clicked.</summary>
    public static readonly StyledProperty<ICommand?> LinkCommandProperty =
        AvaloniaProperty.Register<ConsoleTextView, ICommand?>(nameof(LinkCommand));

    public ConsoleTextView()
    {
        // A transparent fill makes the whole block hit-testable (so drag-selection and "is this press on the
        // text?" work over the gaps between glyphs), and Focusable lets it take/lose focus for the tailing
        // reset. The native selection flyout is dropped — the studio's routed context menu owns right-click.
        Background = Brushes.Transparent;
        Focusable = true;
        ContextFlyout = null;
        Inlines = new InlineCollection();
    }

    public IEnumerable? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public ICommand? LinkCommand
    {
        get => GetValue(LinkCommandProperty);
        set => SetValue(LinkCommandProperty, value);
    }

    /// <inheritdoc/>
    public bool HasSelection => SelectionEnd != SelectionStart;

    /// <inheritdoc/>
    public string CopyText() =>
        HasSelection
            ? SelectedText ?? ""
            : string.Join("\n", (ItemsSource ?? Array.Empty<object>()).OfType<ConsoleLine>().Select(line => line.Text));

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        LostFocus += OnLostFocus;

        _scroll = this.FindAncestorOfType<ScrollViewer>();
        if (_scroll is { })
        {
            _scroll.ScrollChanged += OnScrollChanged;
            // Tunnel so it beats the text block's own press: a press that isn't on the text (scrollbar, gutter)
            // clears the selection; a press on the text is left alone to start a drag-selection.
            _scroll.AddHandler(PointerPressedEvent, OnScrollPressed, RoutingStrategies.Tunnel);
        }

        Register(DataContext as ConsoleViewModel);
        AttachItems(ItemsSource);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        LostFocus -= OnLostFocus;

        if (_scroll is { })
        {
            _scroll.ScrollChanged -= OnScrollChanged;
            _scroll.RemoveHandler(PointerPressedEvent, OnScrollPressed);
            _scroll = null;
        }

        DetachItems();
        Register(null);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        Register(DataContext as ConsoleViewModel);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ItemsSourceProperty)
            AttachItems(ItemsSource);
    }

    private void OnLostFocus(object? sender, RoutedEventArgs e)
    {
        // Reset (clear the selection, resume tailing) only when focus moved to another control in the SAME
        // window — a real "clicked another panel". Focus going to a popup (our own right-click menu, which is
        // its own top-level) must NOT reset, or the selection would vanish before the menu's Copy runs.
        var top = TopLevel.GetTopLevel(this);
        var focused = top?.FocusManager?.GetFocusedElement() as Visual;
        if (focused is null || !ReferenceEquals(TopLevel.GetTopLevel(focused), top))
            return;

        ClearSelection();
        _stick = true;
        ScrollToBottom();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var overLink = LinkAt(e.GetPosition(this)) is not null;
        if (overLink == _cursorIsHand)
            return;

        _cursorIsHand = overLink;
        Cursor = overLink ? HandCursor : BeamCursor;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Route Ctrl+C through the view-model so a keyboard copy uses the shared Clipboard service, same as the
        // right-click Copy — rather than the base text block's own (framework) clipboard path.
        if (e.Key == Key.C && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            (DataContext as ConsoleViewModel)?.CopyAsync().FireAndForget();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        // Remember where (and how) the press landed so the release can tell a click from a drag: the base
        // control extends a selection on any drift, so "did the pointer move?" is what distinguishes them.
        _pressPoint = e.GetPosition(this);
        _pressClickCount = e.ClickCount;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        var press = _pressPoint;
        var clickCount = _pressClickCount;
        _pressPoint = null;
        base.OnPointerReleased(e);

        // A link fires on a plain single click (left button, barely moved). A drag that happened to end on a
        // link is a selection — and a double-click is a word-select — so both are left alone. Gating on
        // movement rather than "is there a selection?" is what makes short link tokens actually clickable:
        // a click that drifts a glyph leaves a one-character selection but is still a click.
        if (e.InitialPressMouseButton != MouseButton.Left || clickCount != 1)
            return;

        var released = e.GetPosition(this);
        if (press is { } start &&
            (Math.Abs(released.X - start.X) > DragThreshold || Math.Abs(released.Y - start.Y) > DragThreshold))
            return;
        if (LinkAt(released) is not { } link)
            return;

        if (LinkCommand?.CanExecute(link) == true)
            LinkCommand.Execute(link);
        ClearSelection();
        e.Handled = true;
    }

    private void Register(ConsoleViewModel? viewModel)
    {
        if (ReferenceEquals(_registeredVm, viewModel))
            return;

        if (_registeredVm is { } previous && ReferenceEquals(previous.Text, this))
            previous.Text = null;

        _registeredVm = viewModel;
        if (viewModel is { })
            viewModel.Text = this;
    }

    private void AttachItems(IEnumerable? items)
    {
        DetachItems();
        if (items is INotifyCollectionChanged incc)
        {
            incc.CollectionChanged += OnItemsChanged;
            _subscribedItems = incc;
        }

        RebuildAll(items);
    }

    private void DetachItems()
    {
        if (_subscribedItems is null)
            return;

        _subscribedItems.CollectionChanged -= OnItemsChanged;
        _subscribedItems = null;
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add when e.NewItems is { }:
                foreach (var item in e.NewItems.OfType<ConsoleLine>())
                    AppendLine(item);
                break;

            case NotifyCollectionChangedAction.Remove when e.OldItems is { }:
                // The console only ever trims from the front, but honour the reported index either way.
                for (var i = 0; i < e.OldItems.Count; i++)
                    RemoveLineAt(Math.Max(0, e.OldStartingIndex));
                break;

            default:
                // Reset (Clear, or a search re-filter) and anything unexpected: rebuild from the source.
                RebuildAll(sender as IEnumerable);
                break;
        }
    }

    private void RebuildAll(IEnumerable? items)
    {
        Inlines?.Clear();
        _lines.Clear();
        if (items is null)
            return;

        foreach (var line in items.OfType<ConsoleLine>())
            AppendLine(line);
    }

    // Appends one line as coloured runs (with an underlined link run for its ConsoleLink span). Every line
    // contributes exactly its text plus a trailing newline, so a hit-test's character index maps cleanly back
    // to (line, offset-in-line).
    private void AppendLine(ConsoleLine line)
    {
        if (Inlines is not { } inlines)
            return;

        var text = line.Text;
        var lineForeground = SeverityBrush(line.Severity);
        var span = new LineSpan(text.Length + 1, line.Link);
        var link = ValidLink(line.Link, text.Length);

        if (link is null)
        {
            inlines.Add(Run(text + "\n", lineForeground, isLink: false));
            span.InlineCount = 1;
        }
        else
        {
            var before = text[..link.Start];
            var linkText = text.Substring(link.Start, link.Length);
            var after = text[(link.Start + link.Length)..] + "\n";

            var count = 0;
            if (before.Length > 0)
            {
                inlines.Add(Run(before, lineForeground, isLink: false));
                count++;
            }

            inlines.Add(Run(linkText, LinkBrush(), isLink: true));
            count++;
            inlines.Add(Run(after, lineForeground, isLink: false));
            count++;
            span.InlineCount = count;
        }

        _lines.Add(span);
    }

    private void RemoveLineAt(int index)
    {
        if (index < 0 || index >= _lines.Count || Inlines is not { } inlines)
            return;

        var inlineOffset = 0;
        for (var i = 0; i < index; i++)
            inlineOffset += _lines[i].InlineCount;

        for (var i = 0; i < _lines[index].InlineCount && inlineOffset < inlines.Count; i++)
            inlines.RemoveAt(inlineOffset);

        _lines.RemoveAt(index);
    }

    private static Run Run(string text, IBrush? foreground, bool isLink)
    {
        var run = new Run(text);
        if (foreground is { })
            run.Foreground = foreground;
        if (isLink)
            run.TextDecorations = Avalonia.Media.TextDecorations.Underline;
        return run;
    }

    // The link under a point, or null. Hit-tests to a character index, then walks the line spans to find which
    // line (and offset within it) the index lands in, and whether that offset is inside the line's link.
    private ConsoleLink? LinkAt(Point point)
    {
        if (TextLayout is not { } layout)
            return null;

        var local = new Point(point.X - Padding.Left, point.Y - Padding.Top);
        // Reject points outside the laid-out text so a hit in the empty gutter (which HitTestPoint would
        // clamp onto the nearest character) can't spuriously light up a link.
        if (local.X < 0 || local.Y < 0 || local.X > layout.Width || local.Y > layout.Height)
            return null;

        // Deliberately NOT gated on hit.IsInside: for this wrapped, multi-run text stream it reads false
        // even directly over a glyph. HitTestPoint's TextPosition still tracks the pointer accurately, and
        // the per-line link-span check below is what authoritatively decides whether a link was hit.
        var hit = layout.HitTestPoint(local);
        var index = hit.TextPosition;
        var offset = 0;
        foreach (var line in _lines)
        {
            if (index < offset + line.Length)
            {
                var inLine = index - offset;
                var link = ValidLink(line.Link, line.Length - 1);
                return link is { } && inLine >= link.Start && inLine < link.Start + link.Length ? link : null;
            }

            offset += line.Length;
        }

        return null;
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_scroll is not { } scroll)
            return;

        if (e.ExtentDelta.Y != 0)
        {
            // New (or removed) content changed the extent: ride the tail only while parked there and not
            // mid-selection — so a growing log doesn't yank a read away, and a scrollbar drag isn't fought.
            if (_stick && !HasSelection)
                ScrollToBottom();
        }
        else if (e.OffsetDelta.Y != 0)
        {
            // The user moved the view: stick iff they parked at the bottom (this is what resumes tailing
            // when they scroll back down, and pauses it when they scroll up or drag the thumb away).
            _stick = scroll.Offset.Y >= scroll.Extent.Height - scroll.Viewport.Height - BottomThreshold;
        }
    }

    private void OnScrollPressed(object? sender, PointerPressedEventArgs e)
    {
        // A press anywhere in the scroll region that isn't on the text itself — the scrollbar, the gutter below
        // the last line — clears the selection and caret; a press on the text is left to start a selection.
        if (!ReferenceEquals(e.Source, this))
            ClearSelection();
    }

    private void ScrollToBottom()
    {
        if (_scroll is not { } scroll)
            return;

        var maxY = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
        scroll.Offset = new Vector(scroll.Offset.X, maxY);
    }

    // A ConsoleLink is only usable if its span sits within the text; a malformed one (from bad prefix parsing)
    // degrades to plain text rather than throwing.
    private static ConsoleLink? ValidLink(ConsoleLink? link, int textLength) =>
        link is { } l && l.Start >= 0 && l.Length > 0 && l.Start + l.Length <= textLength ? link : null;

    private IBrush? SeverityBrush(ConsoleSeverity severity) => severity switch
    {
        ConsoleSeverity.Accent => Resource("ThemeInfoBrush"),
        ConsoleSeverity.Warning => Resource("ThemeWarningBrush"),
        ConsoleSeverity.Error => Resource("ThemeErrorBrush"),
        _ => null,
    };

    private IBrush? LinkBrush() => Resource("ThemePrimaryBrush");

    private IBrush? Resource(string key) =>
        this.TryFindResource(key, out var value) ? value as IBrush : null;

    // Parallel record for one rendered line: its flat length (text + newline), its inline count, and its link.
    private sealed class LineSpan(int length, ConsoleLink? link)
    {
        public int Length { get; } = length;

        public ConsoleLink? Link { get; } = link;

        public int InlineCount { get; set; }
    }
}
