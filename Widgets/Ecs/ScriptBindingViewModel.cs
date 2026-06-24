using Toybox.Studio.Utils;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;
using Toybox.Studio.Services.Scripting;
using Toybox.Studio.Services.EngineApi;
using Toybox.Studio.Services.Project;
using Toybox.Studio.Widgets.PropertyGrid;
using Toybox.Studio.Widgets.ScriptEditor;

namespace Toybox.Studio.Widgets.Ecs;

/// <summary>
/// One script binding inside a <see cref="ScriptContainerViewModel"/>, presented like its own component: a
/// header naming the bound script (resolved through the <see cref="AssetCatalog"/>) with an enable toggle, and
/// a body of that script's property <see cref="Overrides"/> rendered as ordinary grid fields. An override is,
/// by definition, a value the user set away from the script's default, so every override row shows the "set"
/// (non-default) indicator. Editing an override — or toggling <see cref="Enabled"/> — mutates the live backing
/// JSON and re-commits the whole container through the supplied commit action.
/// </summary>
public sealed partial class ScriptBindingViewModel : ObservableObject
{
    private const string ScriptField = "script";
    private const string EnabledField = "enabled";
    private const string OverridesField = "overrides";

    private readonly JValue? _enabled;
    private readonly Action _commit;
    private readonly AssetCatalog? _catalog;
    private readonly long _scriptId;

    private bool _enabledValue;

    private string _filter = "";

    // The bound script's C++ source, resolved lazily (and once) through ScriptEditing; null when there's no
    // open project, the catalog hasn't named the script yet, or no matching file exists.
    private bool _sourceLookupDone;
    private string? _sourcePath;

    public ScriptBindingViewModel(PropertyNode binding, Action commit, AssetCatalog? catalog)
    {
        _commit = commit;
        _catalog = catalog;

        var scriptNode = Find(binding.Children, ScriptField);
        var enabledNode = Find(binding.Children, EnabledField);
        var overridesNode = Find(binding.Children, OverridesField);

        _scriptId = scriptNode?.Value?.Value<long?>() ?? 0;
        _enabled = enabledNode?.Value as JValue;
        _enabledValue = _enabled?.Value<bool>() ?? true;
        Title = ResolveTitle();

        Overrides = [];
        if (overridesNode is not null)
        {
            foreach (var node in overridesNode.Children)
            {
                var field = PropertyViewModelFactory.Create(node, commit);
                // An override exists precisely because its value was set away from the script default, so it
                // is always "set" — the engine carries no per-override default flag to derive this from.
                field.IsModified = true;
                Overrides.Add(field);
            }
        }

        if (catalog is not null)
            catalog.Changed += OnCatalogUpdated;
    }

    /// <summary>The bound script's display name (or its raw id until the asset catalog loads).</summary>
    [ObservableProperty]
    public partial string Title { get; private set; }

    /// <summary>The script's overridden properties, each an ordinary editable grid field.</summary>
    public ObservableCollection<PropertyViewModel> Overrides { get; }

    public bool HasOverrides => Overrides.Count > 0;

    /// <summary>True when the bound script's C++ source was found, so the Source section and Pop out are usable.</summary>
    public bool HasSource
    {
        get
        {
            EnsureSourceResolved();
            return _sourcePath is not null;
        }
    }

    /// <summary>Whether the inline source editor is expanded under this binding's fields.</summary>
    [ObservableProperty]
    public partial bool IsSourceExpanded { get; set; }

    /// <summary>The inline editor, created lazily when the Source section is first expanded.</summary>
    [ObservableProperty]
    public partial InlineScriptEditorViewModel? Inline { get; private set; }

    /// <summary>Set when the inline editor can't be created (asset server / file failure); shown in the section.</summary>
    [ObservableProperty]
    public partial string? SourceError { get; private set; }

    /// <summary>The shared hot-reload toggle the lightning-bolt control binds to (null before startup wiring).</summary>
    public ScriptHotReload? HotReload => ScriptEditing.Current?.HotReload;

    /// <summary>Header icon — matches the script-container component's [[tbx::icon]].</summary>
    public string Icon => "ScrollText";

    public string IconColor => "GREEN";

    /// <summary>Whether this binding runs. Toggling mutates the backing JSON in place and re-commits.</summary>
    public bool Enabled
    {
        get => _enabledValue;
        set
        {
            if (!SetProperty(ref _enabledValue, value) || _enabled is null)
                return;

            _enabled.Value = value;
            _commit();
        }
    }

    /// <summary>The inspector search, pushed down by the container; drives this card's visibility.</summary>
    public string Filter
    {
        get => _filter;
        set
        {
            if (SetProperty(ref _filter, value))
                OnPropertyChanged(nameof(TitleMatchesFilter));
        }
    }

    /// <summary>
    /// True when the search is empty or the script's title matches it. The card stays visible (header and all)
    /// in that case even when no override row matches — ORed in the view with the override grid's own match,
    /// so a binding with no overrides still shows under an empty search.
    /// </summary>
    public bool TitleMatchesFilter =>
        string.IsNullOrWhiteSpace(Filter)
        || Title.Contains(Filter, StringComparison.OrdinalIgnoreCase);

    /// <summary>Opens the bound script asset (raises <see cref="AssetCatalog.AssetActivated"/>).</summary>
    [RelayCommand]
    private void Open()
    {
        if (_scriptId != 0)
            _catalog?.Activate(_scriptId);
    }

    /// <summary>Pops the bound script's source out into the dockable script editor window.</summary>
    [RelayCommand]
    private void PopOut()
    {
        EnsureSourceResolved();
        if (_sourcePath is { } path)
            ScriptEditing.Current?.PopOut(path);
    }

    // Driven by the section header toggle: expanding builds the inline editor (the container shows it in its
    // fill region), collapsing tears it down.
    partial void OnIsSourceExpandedChanged(bool value)
    {
        if (value)
            ExpandSource();
        else
            CollapseSource();
    }

    private void ExpandSource()
    {
        if (Inline is not null)
            return;

        EnsureSourceResolved();
        if (_sourcePath is null || ScriptEditing.Current is not { } editing)
        {
            SourceError = "Script source not found.";
            return;
        }

        var created = editing.CreateInline(_sourcePath);
        if (created)
        {
            SourceError = null;
            Inline = created.Value;
        }
        else
        {
            SourceError = created.Error;
        }
    }

    private void CollapseSource()
    {
        var inline = Inline;
        Inline = null;
        inline?.Dispose();
        SourceError = null;
    }

    private void EnsureSourceResolved()
    {
        if (_sourceLookupDone)
            return;

        _sourceLookupDone = true;
        var name = _catalog?.ResolveName(_scriptId);
        if (ScriptEditing.Current is { } editing && !string.IsNullOrEmpty(name))
        {
            var resolved = editing.ResolveSource(name);
            _sourcePath = resolved ? resolved.Value : null;
        }
    }

    private void OnCatalogUpdated() => Dispatch.To(DispatchContext.UI, () =>
    {
        Title = ResolveTitle();
        // The catalog may have only just named the script (and so its source can now resolve); re-evaluate.
        _sourceLookupDone = false;
        OnPropertyChanged(nameof(HasSource));
    });

    private string ResolveTitle()
    {
        if (_scriptId == 0)
            return "(no script)";

        return _catalog?.ResolveName(_scriptId) ?? $"#{_scriptId}";
    }

    private static PropertyNode? Find(IReadOnlyList<PropertyNode> nodes, string name)
    {
        foreach (var node in nodes)
            if (node.Name == name)
                return node;

        return null;
    }
}
