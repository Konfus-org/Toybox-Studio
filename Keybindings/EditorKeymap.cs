using Avalonia.Input;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using Toybox.Studio.EngineApi.Types.Assets;
using Toybox.Studio.Events;
using Toybox.Studio.Logging;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Keybindings;

/// <summary>
/// The editor's own keybindings, as a real <c>.inputmap</c> document (the same asset format the
/// engine loads for game input) in the user's .toybox folder. The in-memory map is always complete:
/// the registry's registrations generate the default schemes, and the file's entries overlay them —
/// so a user's chords survive while newly registered actions appear with their defaults, and no merge
/// step is ever needed when defaults evolve. Editing is <see cref="ApplyAsync"/> (the keybindings
/// page lands a whole edited scheme set) or <see cref="ResetToDefaultsAsync"/>; each commit
/// dispatches one <see cref="KeybindingsChanged"/>.
/// </summary>
public sealed class EditorKeymap
{
    private readonly ActionRegistry _registry;
    private readonly EventDispatcher _events;
    private readonly Logger _log;
    private readonly PathsCatalog _paths;

    public EditorKeymap(ActionRegistry registry, EventDispatcher events, Logger log, PathsCatalog paths)
    {
        _registry = registry;
        _events = events;
        _log = log;
        _paths = paths;
    }

    /// <summary>The keymap file, editor-global like the rest of ~/.toybox — a valid .inputmap the
    /// engine could load verbatim.</summary>
    public string FilePath => _paths.EditorKeymapFile;

    /// <summary>The complete scheme set: every registered action, carrying the user's bindings where
    /// the file has them and the registered defaults elsewhere.</summary>
    public IReadOnlyList<InputScheme> Schemes { get; private set; } = [];

    /// <summary>
    /// (Re)builds the map from the current registrations overlaid with the keymap file. Called once
    /// at startup, after the composition root's actions are registered; dispatches
    /// <see cref="KeybindingsChanged"/> so hints populate.
    /// </summary>
    public void Reload()
    {
        Schemes = Overlay(BuildDefaults(), LoadFile());
        _events.Dispatch(new KeybindingsChanged());
    }

    /// <summary>The action's first bound chord, or null while unbound.</summary>
    public KeyChordInputControl? FirstChordFor(string actionId)
    {
        foreach (var scheme in Schemes)
        {
            if (scheme.GetAction(actionId) is not { } action)
                continue;

            foreach (var binding in action.Bindings)
                if (binding.Control is KeyChordInputControl chord)
                    return chord;
        }

        return null;
    }

    /// <summary>The action's first chord as a menu shortcut hint, or null while unbound.</summary>
    public KeyGesture? GestureFor(string actionId) =>
        FirstChordFor(actionId) is { } chord ? KeyChordGestures.ToGesture(chord) : null;

    /// <summary>
    /// The keymap as plain data the settings grid reflects — one <see cref="Keybinding"/> per
    /// registered action, carrying its current chord. The list is read-only (the registry defines
    /// the editor's actions; only each entry's chord is editable). Land an edited list with
    /// <see cref="ApplyKeybindingsAsync"/>.
    /// </summary>
    public IReadOnlyList<Keybinding> CreateKeybindings() =>
        _registry.All
            .Select(action => new Keybinding(
                action.Id, action.DefaultChords.Count > 0 ? action.DefaultChords[0] : null)
            {
                Chord = FirstChordFor(action.Id),
            })
            .ToList()
            .AsReadOnly();

    /// <summary>
    /// Commits an edited <see cref="CreateKeybindings"/> list: each entry's chord replaces its
    /// action's first chord binding in the action's registered scheme (unknown names land in the
    /// global scheme); any further bindings the file carries (extra chords, mouse/controller
    /// controls) ride along untouched, and actions the list doesn't mention keep theirs.
    /// </summary>
    public Task ApplyKeybindingsAsync(IEnumerable<Keybinding> bindings)
    {
        var schemes = Schemes.ToList();
        foreach (var binding in bindings)
        {
            if (binding.Name.Length == 0)
                continue;

            var schemeName = _registry.Find(binding.Name)?.Scheme ?? Scheme.Global;
            var index = schemes.FindIndex(scheme => scheme.Name == schemeName);
            var scheme = index >= 0
                ? schemes[index]
                : new InputScheme { Name = schemeName, IsActive = schemeName == Scheme.Global };

            var existing = scheme.GetAction(binding.Name)?.Bindings ?? [];
            var first = existing.FirstOrDefault(entry => entry.Control is KeyChordInputControl);
            var replacement = new List<InputBinding>();
            if (binding.Chord is { } chord)
                replacement.Add(new InputBinding { Control = chord });
            replacement.AddRange(existing.Where(entry => !ReferenceEquals(entry, first)));

            scheme = scheme.SetAction(new InputAction
            {
                Name = binding.Name,
                ValueType = InputActionValueType.Button,
                Bindings = replacement,
            });

            if (index >= 0)
                schemes[index] = scheme;
            else
                schemes.Add(scheme);
        }

        return ApplyAsync(schemes);
    }

    /// <summary>
    /// Commits an edited scheme set: swaps it in, announces the change, and persists the whole map.
    /// The JSON is built synchronously on the caller (a race-free snapshot); only the disk write is
    /// awaited.
    /// </summary>
    public async Task ApplyAsync(IReadOnlyList<InputScheme> schemes)
    {
        Schemes = schemes;
        var json = InputSchemeListConverter.WriteDocument(schemes).ToString(Formatting.Indented);
        _events.Dispatch(new KeybindingsChanged());

        Directory.CreateDirectory(_paths.BaseDirectory);
        await File.WriteAllTextAsync(FilePath, json).ConfigureAwait(false);
    }

    /// <summary>Back to every action's registered default chords (and persists that).</summary>
    public Task ResetToDefaultsAsync() => ApplyAsync(BuildDefaults());

    // One scheme per distinct registration scope, actions in registration order, each bound to its
    // registered default chords.
    private List<InputScheme> BuildDefaults()
    {
        var schemes = new List<InputScheme>();
        foreach (var action in _registry.All)
        {
            var index = schemes.FindIndex(scheme => scheme.Name == action.Scheme);
            if (index < 0)
            {
                schemes.Add(new InputScheme
                {
                    Name = action.Scheme,
                    IsActive = action.Scheme == Scheme.Global,
                });
                index = schemes.Count - 1;
            }

            schemes[index] = schemes[index].SetAction(new InputAction
            {
                Name = action.Id,
                ValueType = InputActionValueType.Button,
                Bindings = [.. action.DefaultChords.Select(chord => new InputBinding { Control = chord })],
            });
        }

        return schemes;
    }

    private IReadOnlyList<InputScheme>? LoadFile()
    {
        try
        {
            if (!File.Exists(FilePath))
                return null;

            return InputSchemeListConverter.ReadDocument(JObject.Parse(File.ReadAllText(FilePath)));
        }
        catch (Exception exception)
        {
            // A corrupt keymap falls back to defaults; preserve the file as a breadcrumb instead of
            // letting the next save silently destroy the user's (possibly recoverable) bindings.
            _log.Warning($"The editor keymap could not be read ({exception.Message}); using defaults.");
            PreserveCorruptFile();
            return null;
        }
    }

    // The file's entries override the generated defaults action-by-action (an entry with no bindings
    // IS the user's unbind); schemes the registry doesn't know survive verbatim.
    private static List<InputScheme> Overlay(List<InputScheme> defaults, IReadOnlyList<InputScheme>? file)
    {
        if (file is null)
            return defaults;

        foreach (var fileScheme in file)
        {
            var index = defaults.FindIndex(scheme => scheme.Name == fileScheme.Name);
            if (index < 0)
            {
                defaults.Add(fileScheme);
                continue;
            }

            foreach (var action in fileScheme.Actions)
                defaults[index] = defaults[index].SetAction(action);
        }

        return defaults;
    }

    private void PreserveCorruptFile()
    {
        try
        {
            File.Move(FilePath, FilePath + ".corrupt", overwrite: true);
        }
        catch (Exception exception)
        {
            // Best-effort; if it can't be moved aside, the next save overwrites it anyway.
            _log.Warning($"Could not preserve the corrupt editor keymap: {exception.Message}");
        }
    }
}
