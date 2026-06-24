using Toybox.Studio.Utils;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Toybox.Studio.Utils.Extensions;
using Toybox.Studio.Services.Dialogs;
using Toybox.Studio.Services.EngineApi;
using Toybox.Studio.Services.Scripting;
using Toybox.Studio.Services.Logging;
using Toybox.Studio.Services.Project;
using Toybox.Studio.Services.Settings;
using Toybox.Studio.Services.Theming;

namespace Toybox.Studio.Widgets.ScriptEditor;

/// <summary>
/// The popped-out script editor: a single dockable window hosting a Monaco editor with a tab per open
/// document. It owns one <see cref="MonacoSession"/> (the bridge to its WebView) and keeps the tab strip in
/// step with the editor — opening a file adds a tab and pushes the buffer into Monaco, selecting a tab swaps
/// the visible model, and edits flow back into the shared <see cref="ScriptDocument"/> so the dirty dot and
/// the inline inspector strip stay consistent. Being a singleton dockable, the same instance is reused every
/// time a script is popped out, so the window accumulates tabs rather than spawning duplicate windows.
/// </summary>
public sealed partial class ScriptEditorViewModel : ObservableObject, IDisposable
{
    private readonly ScriptDocumentService _documents;
    private readonly ProjectManager _projects;
    private readonly Locator _locator;
    private readonly ScriptHotReload _hotReload;
    private readonly ThemeManager _theme;
    private readonly SettingsManager _settings;
    private readonly Logger _log;
    private readonly Dictionary<string, ScriptTabViewModel> _byPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Action> _reloadHandlers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Action<object?>> _externalEditHandlers =
        new(StringComparer.OrdinalIgnoreCase);

    private ClangdSession? _clangd;
    private bool _clangdAttempted;
    private IDisposable? _settingsSubscription;

    // Raw literal at column 0 so the art's leading spaces (its shape) are preserved verbatim.
    private const string GhostArt =
"""

                          ..:::::.........
                      .:::::::................
                   :::::::::.....................
                 :::::::::.........................
               :::::::::::...........................
              ::::::::::::............................
            .::::::::::++:::...........................
           .:::::::::::$$$$$:..........+$$$XXX..........
           :::::::::::::::$$XX.......::$$$XXX$...........
          :::::::::::::$$$$$::.......::X$$$$$$............
          :::::::::::::xx:::::........::;+x;..............
         .::::::::::::::::::::............................
         ::::::::::::::::::::::............................
         :::::::::::::$$$$$$$$$$$$$$$$$$$$$$$$.............
 ........:::::::::::::$$$$$$$$$$$$$$$$$$$$$$$$.....................
::::::::::::::::::::::;$$$$$$$$$$$$$$$$$$$$$$+......................
 ::::::::::::::::::::::X$$XXXXXXXXXXXXXXXX$$$......................
   :::::::::::::::::::::xXXXXXXXXXXXXXXXx+xX.....................
    .:::::::::::::::::::;;XXXXXXXXXXXXXX++;....................
      .:::::::::::::::::;;XXXXXXXXXXXXx+++....................
       .::::::::::::::::;;;XXXXXXXXXxx+xx...................
       :::::::::::::::::;;;;XXXXXXXXXXXX:....................
       :::::::::::::::::::;;;;;XXXXXX::::....................
       :::::::::::::::::::::::::::::::::.....................
       ::::::::::::::::::::::::::::::::......................
      .::::::::::::::::::::::::::::::::...::.................
      ::::::::::::::::::::::::::::::::::::::::................
      :::::::::::::::::::::::::::::::::::::::::...............
      ::::::::::::::::::::::::::::::::::::::::::..............
      :::::::::::::::::::::::::::::::::::::::::::.............
     .::::::::::::::::::::::::::::::::::::::::::::............
     ::::::::::::::::::::::::::::::::::::::::::::::............
     ::::::::::::::::::::::::::::::::::::::::::::::::..........
     :::::::::::::;;;;;;;;;;;::::::::::::::;;;;;;::::::........
     ::::::::::::;;;;::;;;;;;;;:::::::::::;;:::;;;;::::::::....
     :;::::::::;:          ;;;;;:::::::::          ;;;::::::::.
      .;;;;;;;.              .;;;;::::.              :;;:::::

""";

    // The page's language client last reported this state ("starting" | "ready" | "error" | "unavailable").
    // Folded with the active tab's language into LanguageStatus, so the bar tracks whichever file is showing.
    private string _lspState = "";

    public ScriptEditorViewModel(
        MonacoAssetServer server, ScriptDocumentService documents, ProjectManager projects,
        Locator locator, ScriptHotReload hotReload, ThemeManager theme, SettingsManager settings, Logger log)
    {
        _documents = documents;
        _projects = projects;
        _locator = locator;
        _hotReload = hotReload;
        _theme = theme;
        _settings = settings;
        _log = log;

        var started = server.EnsureStarted();
        if (!started)
        {
            ErrorMessage = started.Error;
            _log.Error($"Script editor unavailable: {started.Error}");
            return;
        }

        Session = new MonacoSession(new Uri(started.Value!, "index.html"));
        Session.ContentChanged += OnContentChanged;
        Session.CursorMoved += OnCursorMoved;
        Session.SaveRequested += OnSaveRequested;
        Session.LspStatusChanged += OnLspStatusChanged;
        // Buffered until the page reports ready, so it's safe to set these before the view is even shown.
        Session.SetTheme(EditorTheme.IsDark(_theme));
        // Apply the Scripting editor settings now and on every change (font size, word wrap, minimap).
        _settingsSubscription = _settings.Listen(ApplyEditorOptions);
        // Follow live theme switches (this is a long-lived singleton, so the subscription is released in Dispose).
        _theme.ThemeChanged += OnThemeChanged;
    }

    /// <summary>The bridge to this window's WebView; null when the asset server couldn't start.</summary>
    public MonacoSession? Session { get; }

    /// <summary>The shared hot-reload toggle the lightning-bolt control binds to.</summary>
    public ScriptHotReload HotReload => _hotReload;

    /// <summary>ASCII ghost shown in the center of the editor when nothing is open.</summary>
    public string EmptyGhost => GhostArt;

    public ObservableCollection<ScriptTabViewModel> Tabs { get; } = [];

    /// <summary>The tab whose document is shown. Two-way bound to the tab strip's selection.</summary>
    [ObservableProperty]
    public partial ScriptTabViewModel? ActiveTab { get; set; }

    /// <summary>Set when the editor can't run (asset server failure); shown in place of the editor.</summary>
    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial string CursorText { get; set; } = "Ln 1, Col 1";

    /// <summary>Language (and, when one backs it, language-server) state shown at the left of the status bar.</summary>
    [ObservableProperty]
    public partial string LanguageStatus { get; set; } = "";

    public bool HasTabs => Tabs.Count > 0;

    /// <summary>Show the "nothing open" hint only when there are no tabs and the editor itself is healthy.</summary>
    public bool ShowEmptyState => !HasTabs && string.IsNullOrEmpty(ErrorMessage);

    /// <summary>
    /// Opens <paramref name="path"/> in a tab (focusing it if already open) and shows its buffer in Monaco.
    /// The path is the absolute script source file; the shared buffer is loaded on first open.
    /// </summary>
    public void Open(string path)
    {
        if (Session is null)
            return;

        EnsureClangd();

        var full = Path.GetFullPath(path);
        if (_byPath.TryGetValue(full, out var already))
        {
            ActiveTab = already;
            return;
        }

        var opened = _documents.GetOrOpen(full);
        if (!opened)
        {
            _log.Error(opened.Error ?? $"Couldn't open '{full}'.");
            Popups.ShowErrorAsync("Couldn't open script", opened.Error ?? full).FireAndForget();
            return;
        }

        var document = opened.Value!;
        var tab = new ScriptTabViewModel(document, Close);
        _byPath[full] = tab;
        Tabs.Add(tab);
        NotifyState();

        Session.OpenDocument(document.Path, document.Text, document.Language.Id);

        // A genuine disk reload (ReplaceFromDisk) replaces every tab's content wholesale — undo/scroll loss is
        // unavoidable and correct there. We DON'T re-push on cosmetic Reloaded; only on a real reload.
        void OnReloaded() => Session.OpenDocument(document.Path, document.Text, document.Language.Id);
        document.Reloaded += OnReloaded;
        _reloadHandlers[full] = OnReloaded;

        // When the OTHER live surface (the inline strip, or another tab of this same window can't happen) edits
        // the shared buffer, re-sync this window's model so the two don't diverge — but only when the edit
        // originated elsewhere, so we never echo our own typing back and reset the cursor.
        void OnExternalEdit(object? origin)
        {
            if (ReferenceEquals(origin, this))
                return;
            Dispatch.To(DispatchContext.UI, () => Session.OpenDocument(document.Path, document.Text, document.Language.Id));
        }

        document.ExternalEdit += OnExternalEdit;
        _externalEditHandlers[full] = OnExternalEdit;

        ActiveTab = tab;
    }

    public void Dispose()
    {
        _theme.ThemeChanged -= OnThemeChanged;
        _settingsSubscription?.Dispose();
        foreach (var tab in Tabs)
        {
            if (_reloadHandlers.TryGetValue(tab.Path, out var handler))
                tab.Document.Reloaded -= handler;
            if (_externalEditHandlers.TryGetValue(tab.Path, out var externalHandler))
                tab.Document.ExternalEdit -= externalHandler;
            tab.Detach();
        }

        _reloadHandlers.Clear();
        _externalEditHandlers.Clear();
        if (Session is not null)
        {
            Session.ContentChanged -= OnContentChanged;
            Session.CursorMoved -= OnCursorMoved;
            Session.SaveRequested -= OnSaveRequested;
            Session.LspStatusChanged -= OnLspStatusChanged;
        }

        _clangd?.Dispose();
    }

    private void OnThemeChanged() => Session?.SetTheme(EditorTheme.IsDark(_theme));

    private void ApplyEditorOptions()
    {
        var scripting = _settings.Settings.Scripting;
        Session?.SetOptions(
            minimap: scripting.ShowMinimap,
            fontSize: scripting.FontSize,
            wordWrap: scripting.WordWrap ? "on" : "off");
    }

    private void OnLspStatusChanged(string state)
    {
        _lspState = state;
        RefreshLanguageStatus();
    }

    // Recomputes the status text from the active tab's language and the current LSP state. Highlight-only
    // languages (GLSL, JSON) just show their name; a language with a server appends its state once known.
    private void RefreshLanguageStatus() => Dispatch.To(DispatchContext.UI, () =>
    {
        var language = ActiveTab?.Document.Language;
        if (language is null)
        {
            LanguageStatus = "";
            return;
        }

        if (!language.UsesLanguageServer)
        {
            LanguageStatus = language.DisplayName;
            return;
        }

        var server = language.LanguageServer;
        LanguageStatus = _lspState switch
        {
            "starting" => $"{language.DisplayName} · {server} starting…",
            "ready" => $"{language.DisplayName} · {server} ready",
            "error" => $"{language.DisplayName} · {server} error",
            "unavailable" => $"{language.DisplayName} · no {server}",
            _ => language.DisplayName,
        };
    });

    // Starts clangd once, the first time a script is opened (a project is certainly open by then). On failure
    // the editor keeps working with syntax highlighting only; the reason is logged, not surfaced as an error.
    private void EnsureClangd()
    {
        if (_clangdAttempted || Session is null)
            return;

        _clangdAttempted = true;
        if (_projects.CurrentProject is not { } project)
            return;

        var started = ClangdSession.Start(Session, project.RootDirectory, _locator.EngineSourcePath, _log);
        if (!started)
        {
            OnLspStatusChanged("unavailable");
            _log.Info($"Script editor: {started.Error}");
            return;
        }

        _clangd = started.Value;
        OnLspStatusChanged("starting");
        Session.EnableLsp(new Uri(project.RootDirectory).AbsoluteUri, ScriptLanguages.LanguageServerIds);
        _log.Info("Script editor: clangd attached (engine + sibling-script IntelliSense).");
    }

    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(ShowEmptyState));

    private void NotifyState()
    {
        OnPropertyChanged(nameof(HasTabs));
        OnPropertyChanged(nameof(ShowEmptyState));
    }

    partial void OnActiveTabChanged(ScriptTabViewModel? oldValue, ScriptTabViewModel? newValue)
    {
        if (oldValue is not null)
            oldValue.IsActive = false;
        if (newValue is null)
            return;

        newValue.IsActive = true;
        Session?.SetActive(newValue.Path);
        // The status bar tracks the active tab's language, so refresh it whenever the visible tab changes.
        RefreshLanguageStatus();
    }

    private void Close(ScriptTabViewModel tab)
    {
        Session?.CloseDocument(tab.Path);
        tab.Detach();
        if (_reloadHandlers.Remove(tab.Path, out var handler))
            tab.Document.Reloaded -= handler;
        if (_externalEditHandlers.Remove(tab.Path, out var externalHandler))
            tab.Document.ExternalEdit -= externalHandler;

        var index = Tabs.IndexOf(tab);
        Tabs.Remove(tab);
        _byPath.Remove(tab.Path);
        NotifyState();

        if (ActiveTab == tab)
            ActiveTab = Tabs.Count == 0 ? null : Tabs[Math.Min(index, Tabs.Count - 1)];
    }

    private void OnContentChanged(string path, string text, int version)
    {
        if (_byPath.TryGetValue(path, out var tab))
            tab.Document.SetFromEditor(text, origin: this);
    }

    private void OnCursorMoved(int line, int column) =>
        Dispatch.To(DispatchContext.UI, () => CursorText = $"Ln {line}, Col {column}");

    private async void OnSaveRequested(string path, string text)
    {
        if (!_byPath.TryGetValue(path, out var tab))
            return;

        var document = tab.Document;
        document.SetFromEditor(text, origin: this);
        // Serialise saves to this file across both surfaces so two quick Ctrl+S don't race the same path.
        var saved = await document
            .RunSaveAsync(() => _documents.SaveAsync(document, CancellationToken.None))
            .ContinueOnSameContext();
        if (!saved)
        {
            _log.Error(saved.Error ?? $"Couldn't save '{path}'.");
            await Popups.ShowErrorAsync("Couldn't save script", saved.Error ?? path).ContinueOnSameContext();
            return;
        }

        _log.Info($"Saved {tab.Title}");
        _hotReload.NotifySaved(document.Path);
    }
}
