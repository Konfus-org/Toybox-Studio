using Toybox.Studio.Utils;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;
using Toybox.Studio.Scripting;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Project;
using Toybox.Studio.PropertyGrid;
using Toybox.Studio.ScriptEditor;

namespace Toybox.Studio.Ecs;

/// <summary>
/// One script binding inside a <see cref="ScriptContainerViewModel"/>, presented like its own component: a
/// header naming the bound script (resolved through the <see cref="AssetCatalog"/>) with an enable toggle and a
/// remove "✕", and a body of that script's <see cref="Overrides"/> — its full property set rendered as ordinary
/// grid fields, each showing whether it's set away from the script default. Editing a field — or toggling
/// <see cref="Enabled"/> — mutates the live backing JSON and re-commits the whole container through the supplied
/// commit action; the "✕" detaches the binding through the supplied remove action.
/// </summary>
public sealed partial class ScriptBindingViewModel : ObservableObject
{
    private const string ScriptField = "script";
    private const string EnabledField = "enabled";
    private const string OverridesField = "overrides";

    private readonly IValueAccessor? _enabled;
    private readonly Action _commit;
    private readonly Action _remove;
    private readonly AssetCatalog? _catalog;
    private readonly ulong _scriptId;

    private bool _enabledValue;

    private string _filter = "";

    // The bound script's C++ source, resolved lazily (and once) through ScriptEditing; null when there's no
    // open project, the catalog hasn't named the script yet, or no matching file exists.
    private bool _sourceLookupDone;
    private string? _sourcePath;

    public ScriptBindingViewModel(
        PropertyDescriptor binding, IValueAccessor accessor, Action commit, Action remove, AssetCatalog? catalog)
    {
        _commit = commit;
        _remove = remove;
        _catalog = catalog;

        var scriptDescriptor = Find(binding.Children, ScriptField);
        var enabledDescriptor = Find(binding.Children, EnabledField);
        var overridesDescriptor = Find(binding.Children, OverridesField);

        _scriptId = scriptDescriptor is null ? 0 : ReadId(accessor.Member(scriptDescriptor));
        _enabled = enabledDescriptor is null ? null : accessor.Member(enabledDescriptor);
        _enabledValue = _enabled?.Get() is true;
        Title = ResolveTitle();

        Overrides = [];
        if (overridesDescriptor is not null)
        {
            var overridesAccessor = accessor.Member(overridesDescriptor);
            foreach (var fieldDescriptor in overridesDescriptor.Children)
            {
                // The describe carries the whole script field set (every property, not just the ones already
                // overridden), each with its current value, an is_default flag, and the script default. The
                // base view-model seeds its set/default indicator from is_default; editing or resetting a
                // field re-derives that locally (scripts have no per-field reflect.isDefault) and re-commits.
                var fieldAccessor = overridesAccessor.Member(fieldDescriptor);
                // The inline describe default as a bare token (a script binding's override field carries one).
                var def = (fieldAccessor as JsonAccessor)?.Default is { } clr
                    ? EngineSyncValue.WriteBare(clr)
                    : null;
                PropertyViewModel field = null!;
                field = PropertyViewModelFactory.Create(fieldDescriptor, fieldAccessor);
                // Route the field's commit through the local set/default derivation, then the container commit.
                field.CommitChanges = () => OnFieldCommitted(field, def);
                if (def is not null && !fieldDescriptor.ReadOnly)
                    field.ResetToDefault = () => field.ApplyValue(def.DeepClone());
                Overrides.Add(field);
            }
        }

        if (catalog is not null)
            catalog.Changed += OnCatalogUpdated;
    }

    // A field edit — or a reset, which goes through the same ApplyValue path — routes here: re-derive this
    // field's set/default indicator from its current value against the script default (scripts have no
    // per-field reflect.isDefault), then re-commit the container. The container makes the persisted blob lean,
    // dropping any field back at its default.
    private void OnFieldCommitted(PropertyViewModel field, JToken? def)
    {
        if (def is not null)
            field.IsModified = field.CurrentValue is { } current && !JToken.DeepEquals(current, def);
        _commit();
    }

    /// <summary>Detaches this script binding from the entity (the card header's "✕").</summary>
    [RelayCommand]
    private void Remove() => _remove();

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
    public ScriptHotReloadViewModel? HotReload => ScriptEditing.Current?.HotReload;

    /// <summary>Header icon — matches the script-container component's [[tbx::icon]].</summary>
    public Icon Icon => Icon.ScrollText;

    public Avalonia.Media.Color? IconColor => Toybox.Studio.Utils.Colors.Green;

    /// <summary>Whether this binding runs. Toggling mutates the backing JSON in place and re-commits.</summary>
    public bool Enabled
    {
        get => _enabledValue;
        set
        {
            if (!SetProperty(ref _enabledValue, value) || _enabled is null)
                return;

            _enabled.Set(value);
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

    private static PropertyDescriptor? Find(IReadOnlyList<PropertyDescriptor> descriptors, string name)
    {
        foreach (var descriptor in descriptors)
            if (descriptor.Name == name)
                return descriptor;

        return null;
    }

    // The bound script's id, however the value arrives: an AssetHandle, a bare ulong, or a bare integer token.
    private static ulong ReadId(IValueAccessor accessor) => accessor.Get() switch
    {
        AssetHandle handle => handle.Id,
        ulong id => id,
        long id => (ulong)id,
        int id => (ulong)id,
        { } other => EngineSyncValue.WriteBare(other).Value<ulong?>() ?? 0,
        _ => 0,
    };
}
