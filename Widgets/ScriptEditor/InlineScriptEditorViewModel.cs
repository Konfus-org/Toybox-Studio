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
/// Unlike the dockable editor it deliberately does not subscribe to <see cref="ScriptDocument.Reloaded"/>:
/// the inspector recreates binding cards (and this VM) on every refresh with no disposal hook, so a handler
/// left on the long-lived shared document would pin a dead VM. The trade-off is that an external disk reload
/// won't live-update an open inline strip until it's reopened — the popped-out window covers that case.
/// </summary>
public sealed partial class InlineScriptEditorViewModel : ObservableObject, IDisposable
{
    private readonly ScriptDocumentService _documents;
    private readonly ScriptHotReload _hotReload;
    private readonly Dictionary<string, ScriptDocument> _byPath;

    public InlineScriptEditorViewModel(
        Uri pageUri, IReadOnlyList<ScriptDocument> documents,
        ScriptDocumentService documentService, ScriptHotReload hotReload)
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
        Session.SetTheme(dark: true);
        Session.SetOptions(minimap: false, fontSize: 12, lineNumbers: "on");
        foreach (var document in documents)
            Session.OpenDocument(document.Path, document.Text, document.Language);

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
            document.SetFromEditor(text);
    }

    private void OnCursorMoved(int line, int column) =>
        Dispatch.To(DispatchContext.UI, () => CursorText = $"Ln {line}, Col {column}");

    private async void OnSaveRequested(string path, string text)
    {
        if (!_byPath.TryGetValue(path, out var document))
            return;

        document.SetFromEditor(text);
        var saved = await _documents.SaveAsync(document, CancellationToken.None).ContinueOnSameContext();
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
    }
}
