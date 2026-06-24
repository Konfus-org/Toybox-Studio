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
public sealed partial class ScriptContainerViewModel : ObservableObject
{
    private const string ScriptsProperty = "scripts";

    private readonly Component _component;
    private readonly JObject _raw;
    private readonly Func<Task> _resync;
    private readonly Action _onEdited;

    private string? _filter;

    // Guards the cross-binding reconcile from re-entering when it collapses the other bindings.
    private bool _settlingExpansion;

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
            {
                ScriptBindingViewModel card = null!;
                card = new ScriptBindingViewModel(
                    binding, CommitScripts, () => RemoveBinding(card), PropertyViewRegistry.Assets);
                card.PropertyChanged += OnBindingChanged;
                Bindings.Add(card);
            }
        }
    }

    public ObservableCollection<ScriptBindingViewModel> Bindings { get; }

    public bool HasBindings => Bindings.Count > 0;

    // Keeps a single source section open at a time: when one binding expands its Source editor (rendered in
    // its own card), the others collapse so only one inline editor is ever open.
    private void OnBindingChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ScriptBindingViewModel.IsSourceExpanded) || _settlingExpansion)
            return;

        _settlingExpansion = true;
        if (sender is ScriptBindingViewModel expanded && expanded.IsSourceExpanded)
        {
            foreach (var other in Bindings)
                if (!ReferenceEquals(other, expanded))
                    other.IsSourceExpanded = false;
        }

        _settlingExpansion = false;
    }

    // Detaches one script binding (its card's "✕"): drop it from the live scripts array and re-commit the
    // now-shorter array. Removing the LAST binding instead deletes the whole — now empty — script_container
    // component, so the Scripts tab returns to its empty state and nothing empty persists.
    private async void RemoveBinding(ScriptBindingViewModel card)
    {
        var index = Bindings.IndexOf(card);
        if (index < 0)
            return;

        card.PropertyChanged -= OnBindingChanged;
        Bindings.RemoveAt(index);
        OnPropertyChanged(nameof(HasBindings));

        if (Bindings.Count == 0)
        {
            var result = await _component.RemoveAsync(CancellationToken.None).ContinueOnSameContext();
            if (result.Success)
                _onEdited();
            else
                await Popups.ShowErrorAsync("Couldn't remove script", result.Error!).ContinueOnSameContext();
            await _resync().ContinueOnSameContext();
            return;
        }

        // Drop the binding from the live array so the re-sent (lean) array no longer carries it.
        if (ScriptsArray() is { } array && index < array.Count)
            array[index].Remove();
        CommitScripts();
    }

    // The live scripts array inside the raw component ({ scripts: { value: [ … ] } }), or null if absent.
    private JArray? ScriptsArray() =>
        _raw[ScriptsProperty] is JObject typed && typed["value"] is JArray value
            ? value
            : _raw[ScriptsProperty] as JArray;

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

        // The describe path expands each binding's overrides into the script's full field set (every
        // property, with describe-only type/choices/is_default/default metadata) so the inspector can show
        // and edit them all. Only the fields actually set away from the script default may persist, as bare
        // { type, value } pairs — so send a lean clone; the live tree keeps the full set for editing.
        var payload = LeanScripts(bare.DeepClone());

        var result = await _component
            .SetPropertyAsync(ScriptsProperty, payload, CancellationToken.None)
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

    // Rewrites each binding's override blob to the lean form the world persists: only the fields whose value
    // differs from the script default survive, as bare { type, value } pairs (the describe-only choices /
    // is_default / default keys are dropped). A freshly attached script — or one reset to all defaults —
    // therefore persists an empty override blob, exactly as a hand-authored lean world would. Mutates and
    // returns the given (already-cloned) tree.
    private static JToken LeanScripts(JToken scripts)
    {
        if (scripts is not JArray bindings)
            return scripts;

        foreach (var binding in bindings.OfType<JObject>())
        {
            // overrides is { …, value: { field: { type, value, choices?, is_default, default } } } in
            // describe form; reach the blob's per-field wrappers.
            if (Inner(binding["overrides"]) is not JObject blob)
                continue;

            var lean = new JObject();
            foreach (var field in blob.Properties())
            {
                if (field.Value is not JObject wrapper || wrapper["value"] is not { } value)
                    continue;

                // Persist only a value the user actually set away from the script default.
                if (wrapper["default"] is { } def && JToken.DeepEquals(value, def))
                    continue;

                var clean = new JObject { ["value"] = value.DeepClone() };
                if (wrapper["type"] is { } type)
                    clean["type"] = type.DeepClone();
                lean[field.Name] = clean;
            }

            SetInner(binding["overrides"], lean);
        }

        return scripts;
    }

    // The value inside a typed field wrapper ({ …, value: … }), or the token itself when already bare.
    private static JToken? Inner(JToken? token) =>
        token is JObject obj && obj.TryGetValue("value", out var value) ? value : token;

    // Replaces an override-blob wrapper's inner value map in place ({ …, value: <map> }); a no-op for a
    // malformed wrapper with no value slot (describe always emits one).
    private static void SetInner(JToken? wrapper, JToken value)
    {
        if (wrapper is JObject obj && obj["value"] is not null)
            obj["value"] = value;
    }
}
