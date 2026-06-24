using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using Toybox.Studio.Services.Scripting;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Widgets.ScriptEditor;

/// <summary>
/// The compact editor embedded under a script's fields in the inspector's Script tab. It drives its own Monaco
/// WebView (via <see cref="Session"/>) but edits the same shared <see cref="ScriptDocument"/> buffers as the
/// popped-out window, so changes and the dirty state stay consistent across both surfaces. A C++ script is a
/// header/implementation pair, so both are opened and a small <c>.h</c>/<c>.cpp</c> toggle (shown only when
/// there are two) switches the visible one — the implementation is active by default. Trimmed for the inline
/// context (no minimap, smaller font) and saved with Ctrl+S like the full editor.
///
/// It deliberately does not subscribe to <see cref="ScriptDocument.Reloaded"/> (a disk reload won't
/// live-update an open inline strip — the popped-out window covers that), but it DOES subscribe to
/// <see cref="ScriptDocument.ExternalEdit"/> so typing in the popped-out window keeps this strip's Monaco
/// model in sync (and vice-versa). That subscription is released in <see cref="Dispose"/>, which the owning
/// binding card calls when the source section collapses, so it never pins a dead VM onto the shared document.
/// </summary>
public sealed partial class InlineScriptEditorViewModel : ObservableObject, IDisposable
{
    private readonly ScriptDocumentService _documents;
    private readonly ScriptHotReload _hotReload;
    private readonly Dictionary<string, ScriptDocument> _byPath;
    private readonly Dictionary<string, Action<object?>> _externalEditHandlers =
        new(StringComparer.OrdinalIgnoreCase);

    public InlineScriptEditorViewModel(
        Uri pageUri, IReadOnlyList<ScriptDocument> documents,
        ScriptDocumentService documentService, ScriptHotReload hotReload, bool dark,
        int fontSize, bool wordWrap)
    {
        _documents = documentService;
        _hotReload = hotReload;
        Documents = new ObservableCollection<ScriptDocument>(documents);
        _byPath = documents.ToDictionary(document => document.Path, StringComparer.OrdinalIgnoreCase);

        Session = new MonacoSession(pageUri);
        Session.Ready += OnReady;
        Session.ContentChanged += OnContentChanged;
        Session.CursorMoved += OnCursorMoved;
        Session.SaveRequested += OnSaveRequested;
        Session.SetTheme(dark);
        Session.SetOptions(
            minimap: false, fontSize: fontSize, lineNumbers: "on", wordWrap: wordWrap ? "on" : "off");
        foreach (var document in documents)
        {
            Session.OpenDocument(document.Path, document.Text, document.Language);
            // Re-sync this strip when the popped-out window edits the same shared buffer (skipping our own
            // edits via the origin token), re-pushing only the document that changed so a background tab's
            // undo/scroll isn't clobbered. Released in Dispose so it never outlives this live surface.
            var captured = document;
            void Handler(object? origin) => OnExternalEdit(captured, origin);
            document.ExternalEdit += Handler;
            _externalEditHandlers[document.Path] = Handler;
        }

        // The pair is ordered header-then-implementation, so the .cpp is last — make it the active one.
        ActiveDocument = documents.Count > 0 ? documents[^1] : null;
    }

    public MonacoSession Session { get; }

    /// <summary>The open files for this script (its header and implementation); drives the file toggle.</summary>
    public ObservableCollection<ScriptDocument> Documents { get; }

    /// <summary>Only show the .h/.cpp toggle when the script actually has both.</summary>
    public bool HasMultipleFiles => Documents.Count > 1;

    /// <summary>The file shown in the editor; two-way bound to the toggle, switches the active Monaco model.</summary>
    [ObservableProperty]
    public partial ScriptDocument? ActiveDocument { get; set; }

    /// <summary>False until the embedded Monaco page has loaded; drives the loading spinner in the card.</summary>
    [ObservableProperty]
    public partial bool IsReady { get; private set; }

    [ObservableProperty]
    public partial string CursorText { get; set; } = "Ln 1, Col 1";

    partial void OnActiveDocumentChanged(ScriptDocument? value)
    {
        if (value is not null)
            Session.SetActive(value.Path);
    }

    private void OnReady() => Dispatch.To(DispatchContext.UI, () => IsReady = true);

    private void OnContentChanged(string path, string text, int version)
    {
        if (_byPath.TryGetValue(path, out var document))
            document.SetFromEditor(text, origin: this);
    }

    // The popped-out window (or another surface) edited a buffer this strip shows: re-push that document's
    // authoritative text so the two don't diverge. Skips our own edits so we never reset our cursor.
    // OpenDocument is safely queued by the session even before the page is ready.
    private void OnExternalEdit(ScriptDocument document, object? origin)
    {
        if (ReferenceEquals(origin, this))
            return;

        Dispatch.To(DispatchContext.UI,
            () => Session.OpenDocument(document.Path, document.Text, document.Language));
    }

    private void OnCursorMoved(int line, int column) =>
        Dispatch.To(DispatchContext.UI, () => CursorText = $"Ln {line}, Col {column}");

    private async void OnSaveRequested(string path, string text)
    {
        if (!_byPath.TryGetValue(path, out var document))
            return;

        document.SetFromEditor(text, origin: this);
        // Serialise saves to this file across both surfaces so two quick Ctrl+S don't race the same path.
        var saved = await document
            .RunSaveAsync(() => _documents.SaveAsync(document, CancellationToken.None))
            .ContinueOnSameContext();
        if (!saved)
        {
            await Toybox.Studio.Services.Dialogs.Popups
                .ShowErrorAsync("Couldn't save script", saved.Error ?? path).ContinueOnSameContext();
            return;
        }

        _hotReload.NotifySaved(document.Path);
    }

    public void Dispose()
    {
        Session.Ready -= OnReady;
        Session.ContentChanged -= OnContentChanged;
        Session.CursorMoved -= OnCursorMoved;
        Session.SaveRequested -= OnSaveRequested;

        foreach (var document in Documents)
            if (_externalEditHandlers.TryGetValue(document.Path, out var handler))
                document.ExternalEdit -= handler;
        _externalEditHandlers.Clear();
    }
}
