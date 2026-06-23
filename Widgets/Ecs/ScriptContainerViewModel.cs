using Toybox.Studio.Utils;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json.Linq;
using Toybox.Studio.Services.Dialogs;
using Toybox.Studio.Services.World;
using Toybox.Studio.Widgets.PropertyGrid;

namespace Toybox.Studio.Widgets.Ecs;

/// <summary>
/// The script-container component, presented not as one generic grid but as a stack of per-script cards: each
/// <see cref="ScriptBindingViewModel"/> reads like its own component (named for the bound script) whose
/// "fields" are that script's property overrides. Any override edit or enable toggle re-sends the whole
/// <c>scripts</c> array — the same single-property round-trip a normal component edit uses — so the engine
/// reapplies the binding.
/// </summary>
public sealed class ScriptContainerViewModel : ObservableObject
{
    private const string ScriptsProperty = "scripts";

    private readonly Component _component;
    private readonly JObject _raw;
    private readonly Func<Task> _resync;
    private readonly Action _onEdited;

    private string? _filter;

    public ScriptContainerViewModel(
        Component component, ComponentDescription snapshot, Func<Task> resync, Action onEdited)
    {
        _component = component;
        _raw = snapshot.Raw;
        _resync = resync;
        _onEdited = onEdited;

        Bindings = [];
        var scripts = snapshot.Properties.FirstOrDefault(property => property.Name == ScriptsProperty);
        if (scripts is not null)
        {
            foreach (var binding in scripts.Children)
                Bindings.Add(new ScriptBindingViewModel(binding, CommitScripts, PropertyViewRegistry.Assets));
        }
    }

    public ObservableCollection<ScriptBindingViewModel> Bindings { get; }

    public bool HasBindings => Bindings.Count > 0;

    /// <summary>The inspector search, fanned out to each binding card.</summary>
    public string? Filter
    {
        get => _filter;
        set
        {
            if (!SetProperty(ref _filter, value))
                return;

            foreach (var binding in Bindings)
                binding.Filter = value ?? "";
        }
    }

    // Re-sends the whole scripts array after an override edit or an enable toggle. Mirrors the component edit
    // path (reflect.set on one top-level property): the live token was already mutated in place, so this just
    // hands the engine the updated array. On rejection it surfaces the message and re-syncs to engine truth.
    private async void CommitScripts()
    {
        var bare = _raw[ScriptsProperty] is JObject typed && typed.TryGetValue("value", out var value)
            ? value
            : _raw[ScriptsProperty];
        if (bare is null)
            return;

        var result = await _component
            .SetPropertyAsync(ScriptsProperty, bare, CancellationToken.None)
            .ContinueOnSameContext();
        if (result.Success)
        {
            _onEdited();
            return;
        }

        await Popups.ShowErrorAsync(
            "Couldn't apply change",
            $"The engine rejected the script change:\n\n{result.Error}")
            .ContinueOnSameContext();
        await _resync().ContinueOnSameContext();
    }
}
