using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Toybox.Studio.Coding.Languages;
using Toybox.Studio.Dialogs;
using Toybox.Studio.Events;
using Toybox.Studio.Logging;
using Toybox.Studio.Monaco;
using Toybox.Studio.Projects;
using Toybox.Studio.Settings;
using Toybox.Studio.Themes;
using Toybox.Studio.Utils;
using Toybox.Studio.Utils.Composition;

namespace Toybox.Studio.Coding;

/// <summary>
/// The generic code editor: a Monaco surface with a tab per open document, self-contained so it can host the
/// dockable <see cref="CoderPanelViewModel"/> or (later) an inline editor strip. It owns one
/// <see cref="MonacoSession"/> (the bridge to its WebView) and keeps the tab strip in step with the editor —
/// opening a file adds a tab and pushes the buffer into Monaco, selecting a tab swaps the visible model, and
/// edits flow back into the shared <see cref="ScriptDocument"/> so the dirty dot stays consistent. It knows
/// nothing about docking or how files are routed to it; a host feeds it paths through <see cref="Open"/>.
/// The editor's light/dark base and primary accent follow the active theme, and clangd (started lazily on the
/// first C++ open) backs C++ with completion / go-to-definition / semantic type colouring.
/// </summary>
public sealed partial class CoderViewModel : ObservableEventSubscriber, IEventHandler<EditorSettingsChanged>
{
    private readonly ScriptService _documents;
    private readonly Project _project;
    private readonly EngineSourceLocator _locator;
    private readonly ThemeManager _theme;
    private readonly SettingsManager _settings;
    private readonly Popups _popups;
    private readonly Logger _log;
    private readonly ViewModelFactory _viewModels;
    private readonly Dictionary<string, CoderTabViewModel> _byPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Action> _reloadHandlers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Action<object?>> _externalEditHandlers =
        new(StringComparer.OrdinalIgnoreCase);

    // Raw literal at column 0 so the art's leading spaces (its shape) are preserved verbatim.
    private const string GhostArt =
"""
             :::::::::::::::::
         .:::::::::::::::::::::::
       .:::::::::::::::::::::::::::.
     .:::::::::::::::::::::::::::::::.
    :::::::::::::::::::::::::::::::::::
   :::::::::::::::::::::::::::::::::::::
  :::::::::::::::::::::::::::::::::::::::
 ::::::::::::::::::::::::::::::::::::::::.
 ::::::::::......::::::::......:::::::::::
.:::::::::........:::::::.......::::::::::
.:::::::::........:::::::........:::::::::.
.::::::::::......::::::::.......::::::::::.
.::::::::::::...:::::::::::..:::::::::::::.
.:::::::::::::::::::::::::::::::::::::::::.
.:::::::::::::::::::::::::::::::::::::::::.
.:::::::::::::::::::::::::::::::::::::::::.
.:::::::::::::::::::::::::::::::::::::::::.
.:::::::::::::::::::::::::::::::::::::::::.
::::::::::::::::::::::::::::::::::::::::::.
.:::::::::::::::::::::::::::::::::::::::::.
::::::::::::::::::::::::::::::::::::::::::.
.:::::::.:::::::::::::::::::::::::.:::::::.
::::::.    :::::::::. .:::::::::    .:::::.
.:::.       .::::::.   .::::::        .:::.
:::           .:::.     .:::.           ::.
.               ..       ..               .
""";

    // Running language servers, keyed by server id (clangd, roslyn); started lazily, one attempt per language.
    private readonly Dictionary<string, ILanguageServer> _servers = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _attemptedLanguages = new(StringComparer.OrdinalIgnoreCase);

    // The last state each server's page client reported ("starting" | "ready" | "error"), keyed by server id.
    // Folded with the active tab's language into LanguageStatus, so the bar tracks whichever file is showing.
    private readonly Dictionary<string, string> _lspStateByServer = new(StringComparer.OrdinalIgnoreCase);

    // Resolves GLSL #include specs to files for the page's include navigation; built on first use.
    private ShaderIncludeResolver? _includeResolver;

    // The Studio's own source root, found once; files under it open read-only (see IsReadOnly).
    private string? _studioRootCache;

    public CoderViewModel(
        MonacoAssetServer server, ScriptService documents, Project project, EngineSourceLocator locator,
        ThemeManager theme, SettingsManager settings, EventDispatcher events, ViewModelFactory viewModels,
        Popups popups, Logger log)
        : base(events)
    {
        _documents = documents;
        _project = project;
        _locator = locator;
        _theme = theme;
        _settings = settings;
        _popups = popups;
        _log = log;
        _viewModels = viewModels;

        HotReload = viewModels.Create<CoderHotReloadViewModel>();

        // HasTabs / ShowGhost / EditorVisible derive from the tab list, so let the collection drive their
        // change notifications instead of hand-poking them on every add/remove.
        Tabs.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasTabs));
            OnPropertyChanged(nameof(ShowGhost));
            OnPropertyChanged(nameof(EditorVisible));
        };

        var started = server.EnsureStarted();
        if (!started)
        {
            ErrorMessage = started.Error;
            _log.Error($"Code editor unavailable: {started.Error}");
            return;
        }

        // The page is loading until it reports ready; the ghost shows "Loading…" until then.
        IsLoading = true;

        Session = new MonacoSession(new Uri(started.Value!, "index.html"));
        Session.Ready += OnReady;
        Session.ContentChanged += OnContentChanged;
        Session.CursorMoved += OnCursorMoved;
        Session.SaveRequested += OnSaveRequested;
        Session.LspStatusChanged += OnLspStatusChanged;
        Session.IncludeResolveRequested += OnIncludeResolveRequested;
        Session.OpenFileRequested += OnOpenFileRequested;
        // Buffered until the page reports ready, so it's safe to set these before the view is even shown.
        Session.SetTheme(_theme.Active.IsDark, PrimaryHex());
        // Apply the Scripting editor settings now; the EditorSettingsChanged handler re-applies on change.
        ApplyEditorOptions();
        // Follow live theme switches (released in Dispose).
        _theme.ThemeChanged += OnThemeChanged;
    }

    /// <summary>The bridge to this surface's WebView; null when the asset server couldn't start.</summary>
    public MonacoSession? Session { get; }

    /// <summary>The hot-reload toggle the lightning-bolt control binds to.</summary>
    public CoderHotReloadViewModel HotReload { get; }

    /// <summary>ASCII ghost shown in the center of the editor when nothing is open or the page is loading.</summary>
    public string EmptyGhost => GhostArt;

    public ObservableCollection<CoderTabViewModel> Tabs { get; } = [];

    /// <summary>The tab whose document is shown. Two-way bound to the tab strip's selection.</summary>
    [ObservableProperty]
    public partial CoderTabViewModel? ActiveTab { get; set; }

    /// <summary>Set when the editor can't run (asset server failure); shown in place of the editor.</summary>
    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    /// <summary>True while the Monaco page is still loading (before it reports ready); the ghost covers it.</summary>
    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string CursorText { get; set; } = "Ln 1, Col 1";

    /// <summary>Language (and, when one backs it, language-server) state shown at the left of the status bar.</summary>
    [ObservableProperty]
    public partial string LanguageStatus { get; set; } = string.Empty;

    public bool HasTabs => Tabs.Count > 0;

    /// <summary>The WebView shows only when a document is open and the page has finished loading — otherwise
    /// its blank buffer would cover the ghost.</summary>
    public bool EditorVisible => Session is not null && HasTabs && !IsLoading;

    /// <summary>Show the ASCII ghost when the editor is healthy and either nothing is open or the page is
    /// still loading.</summary>
    public bool ShowGhost => string.IsNullOrEmpty(ErrorMessage) && (!HasTabs || IsLoading);

    /// <summary>The caption under the ghost: a loading hint while the page comes up, else the empty hint.</summary>
    public string GhostCaption => IsLoading ? "Loading…" : "No script open.";

    /// <summary>
    /// Opens <paramref name="path"/> in a tab (focusing it if already open) and shows its buffer in Monaco.
    /// The path is the absolute source file; the shared buffer is loaded on first open. A <paramref name="line"/>
    /// above 0 scrolls the file to that 1-based line and puts the caret there.
    /// </summary>
    public void Open(string path, int line = 0)
    {
        if (Session is null)
            return;

        var full = Path.GetFullPath(path);
        if (_byPath.TryGetValue(full, out var already))
        {
            ActiveTab = already;
            if (line > 0)
                Session.RevealLine(already.Path, line);
            return;
        }

        var opened = _documents.GetOrOpen(full);
        if (!opened)
        {
            _log.Error(opened.Error ?? $"Couldn't open '{full}'.");
            _popups.ErrorAsync("Couldn't open file", opened.Error ?? full).FireAndForget();
            return;
        }

        var document = opened.Value!;
        // Start the language server that backs this file's language (clangd/Roslyn), once per language.
        EnsureServerFor(document.Language);
        var tab = _viewModels.Create<CoderTabViewModel>(document, (Action<CoderTabViewModel>)Close);
        _byPath[full] = tab;
        Tabs.Add(tab);

        Session.OpenDocument(document.Path, document.Text, document.Language.Id);

        // A genuine disk reload (ReplaceFromDisk) replaces every tab's content wholesale — undo/scroll loss is
        // unavoidable and correct there. We DON'T re-push on cosmetic Reloaded; only on a real reload.
        void OnReloaded() => Session.OpenDocument(document.Path, document.Text, document.Language.Id);
        document.Reloaded += OnReloaded;
        _reloadHandlers[full] = OnReloaded;

        // When another live surface (a future inline strip) edits the shared buffer, re-sync this surface's
        // model so the two don't diverge — but only when the edit originated elsewhere, so we never echo our
        // own typing back and reset the cursor.
        void OnExternalEdit(object? origin)
        {
            if (ReferenceEquals(origin, this))
                return;
            Dispatch.To(DispatchContext.UI, () => Session.OpenDocument(document.Path, document.Text, document.Language.Id));
        }

        document.ExternalEdit += OnExternalEdit;
        _externalEditHandlers[full] = OnExternalEdit;

        ActiveTab = tab;
        // Posted after the open/setActive above (the session preserves order), so Monaco reveals the line once
        // the model exists and is showing.
        if (line > 0)
            Session.RevealLine(document.Path, line);
    }

    public override void Dispose()
    {
        _theme.ThemeChanged -= OnThemeChanged;
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
            Session.Ready -= OnReady;
            Session.ContentChanged -= OnContentChanged;
            Session.CursorMoved -= OnCursorMoved;
            Session.SaveRequested -= OnSaveRequested;
            Session.LspStatusChanged -= OnLspStatusChanged;
            Session.IncludeResolveRequested -= OnIncludeResolveRequested;
            Session.OpenFileRequested -= OnOpenFileRequested;
        }

        HotReload.Dispose();
        foreach (var server in _servers.Values)
            server.Dispose();
        _servers.Clear();
        base.Dispose();
    }

    /// <summary>The setting was applied (font size, word wrap, minimap); push it into the editor.</summary>
    public void Handle(in EditorSettingsChanged evt) => ApplyEditorOptions();

    private void OnReady() => Dispatch.To(DispatchContext.UI, () => IsLoading = false);

    private void OnThemeChanged() => Session?.SetTheme(_theme.Active.IsDark, PrimaryHex());

    // The active theme's primary colour as an "RRGGBB" hex string (no leading '#') for the Monaco accents.
    private string PrimaryHex()
    {
        var color = _theme.Active.Colors.Primary.Representative;
        return $"{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private void ApplyEditorOptions()
    {
        var scripting = _settings.Editor.Scripting;
        Session?.SetOptions(
            minimap: scripting.ShowMinimap,
            fontSize: scripting.FontSize,
            wordWrap: scripting.WordWrap ? "on" : "off");
    }

    private void OnLspStatusChanged(string server, string state)
    {
        _lspStateByServer[server] = state;
        RefreshLanguageStatus();
    }

    // Recomputes the status text from the active tab's language and its server's state. Highlight-only languages
    // (GLSL, JSON) just show their name; a language with a server appends that server's state once known.
    private void RefreshLanguageStatus() => Dispatch.To(DispatchContext.UI, () =>
    {
        var active = ActiveTab;
        var language = active?.Document.Language;
        if (language is null)
        {
            LanguageStatus = string.Empty;
            return;
        }

        var status = language.DisplayName;
        if (language.UsesLanguageServer)
        {
            var serverName = language.LanguageServer;
            var running = _servers.Values.FirstOrDefault(
                server => server.LanguageIds.Contains(language.Id, StringComparer.OrdinalIgnoreCase));
            var state = running is { } && _lspStateByServer.TryGetValue(running.Id, out var reported)
                ? reported
                : running is null && _attemptedLanguages.Contains(language.Id) ? "unavailable" : string.Empty;

            status = state switch
            {
                "starting" => $"{language.DisplayName} · {serverName} starting…",
                "ready" => $"{language.DisplayName} · {serverName} ready",
                "error" => $"{language.DisplayName} · {serverName} error",
                "unavailable" => $"{language.DisplayName} · no {serverName}",
                _ => language.DisplayName,
            };
        }

        if (active is { } && IsReadOnly(active.Path))
            status += " · read-only";
        LanguageStatus = status;
    });

    // Starts the language server that backs this language, at most once per language, the first time a file of
    // it is opened (a project is certainly open by then). Highlight-only languages (GLSL, JSON) have no factory
    // and are skipped. On failure or an unavailable backend the editor keeps working with highlighting only; the
    // reason is logged, not surfaced as an error.
    private void EnsureServerFor(ScriptLanguage language)
    {
        if (Session is null || !_attemptedLanguages.Add(language.Id))
            return;

        if (LanguageServers.ForLanguage(language.Id) is not { } factory)
            return; // No server backs this language.

        // Skip a server we already started for another of its languages (e.g. clangd for both .cpp and .h).
        if (factory.LanguageIds.Any(id => _servers.Values.Any(server => server.LanguageIds.Contains(id))))
            return;

        var context = new LanguageServerContext(_project.Path, EngineRoot(), StudioSourceRoot, _log);
        var started = factory.Start(Session, context);
        if (!started)
        {
            _log.Info($"Code editor: {language.DisplayName} server failed to start: {started.Error}");
            return;
        }

        if (started.Value is not { } server)
        {
            _log.Info($"Code editor: no {language.DisplayName} language server available (highlight-only).");
            return;
        }

        _servers[server.Id] = server;
        _log.Info($"Code editor: {server.Id} attached for {string.Join(", ", server.LanguageIds)}.");
    }

    private string? EngineRoot()
    {
        var engine = _locator.Locate(_project.Path);
        return engine ? engine.Value : null;
    }

    // The Studio's own source root (the directory holding Toybox.Studio.slnx), found once by walking up from the
    // running binary — the Studio is built from source alongside the engine, so it's an ancestor. Empty when it
    // can't be found, in which case the C# server stays highlight-only and nothing is treated as a Studio file.
    private string StudioSourceRoot => _studioRootCache ??= FindStudioRoot();

    private static string FindStudioRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Toybox.Studio.slnx")))
                return dir.FullName;
        }

        return string.Empty;
    }

    // The Studio's own source files open read-only: they're shown for reference (log links, go-to-definition),
    // not edited from inside the running editor. Project scripts and everything else stay editable.
    private bool IsReadOnly(string path)
    {
        var root = StudioSourceRoot;
        if (string.IsNullOrEmpty(root))
            return false;

        var full = Path.GetFullPath(path);
        return full.StartsWith(
            Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    // The page asked to resolve a GLSL #include; answer with the file it points at (or null when unresolved).
    private void OnIncludeResolveRequested(int id, string from, string spec)
    {
        _includeResolver ??= new ShaderIncludeResolver(_project.Path, EngineRoot());
        Session?.ResolveIncludeResult(id, _includeResolver.Resolve(PathFromUri(from), spec));
    }

    // The page asked to open a cross-file definition target (a header, another .cs, a resolved include) in a tab.
    private void OnOpenFileRequested(string uri, int line)
    {
        var path = PathFromUri(uri);
        if (path.Length > 0)
            Open(path, line);
    }

    // Turns a page-supplied file uri (file:///c:/…) into an OS path; passes a plain path through unchanged.
    private static string PathFromUri(string uri) =>
        uri.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
        && Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
            ? parsed.LocalPath
            : uri;

    partial void OnErrorMessageChanged(string? value)
    {
        OnPropertyChanged(nameof(ShowGhost));
        OnPropertyChanged(nameof(EditorVisible));
    }

    partial void OnIsLoadingChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowGhost));
        OnPropertyChanged(nameof(EditorVisible));
        OnPropertyChanged(nameof(GhostCaption));
    }

    partial void OnActiveTabChanged(CoderTabViewModel? oldValue, CoderTabViewModel? newValue)
    {
        if (oldValue is not null)
            oldValue.IsActive = false;
        if (newValue is null)
            return;

        newValue.IsActive = true;
        Session?.SetActive(newValue.Path);
        // Read-only follows the visible tab (one shared editor): Studio source is shown, not edited.
        Session?.SetOptions(readOnly: IsReadOnly(newValue.Path));
        // The status bar tracks the active tab's language, so refresh it whenever the visible tab changes.
        RefreshLanguageStatus();
    }

    private void Close(CoderTabViewModel tab)
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

        // Studio source is read-only; a stray Ctrl+S is a no-op rather than writing to the running editor's code.
        if (IsReadOnly(path))
        {
            _log.Info($"{tab.Title} is a read-only Studio source; not saved.");
            return;
        }

        var document = tab.Document;
        document.SetFromEditor(text, origin: this);
        // Serialise saves to this file so two quick Ctrl+S don't race the same path.
        var saved = await document
            .RunSaveAsync(() => _documents.SaveAsync(document, CancellationToken.None))
            .ContinueOnSameContext();
        if (!saved)
        {
            _log.Error(saved.Error ?? $"Couldn't save '{path}'.");
            await _popups.ErrorAsync("Couldn't save file", saved.Error ?? path).ContinueOnSameContext();
            return;
        }

        _log.Info($"Saved {tab.Title}");
        HotReload.NotifySaved(document.Path);
    }
}
