using Toybox.Studio.Events;

namespace Toybox.Studio.Keybindings;

/// <summary>
/// Every invokable editor action, by id — the registry rebinding is built on: the keymap generates
/// its default schemes from the registrations, the keybindings page lists them, and menus resolve
/// their rows against them. Features register their actions at construction (the composition root
/// builds everything up front) and execute them by handling <see cref="EditorActionInvoked"/>;
/// <see cref="Invoke"/> is the one place that event is published.
/// </summary>
public sealed class ActionRegistry
{
    private readonly object _sync = new();
    private readonly List<EditorAction> _actions = [];
    private readonly Dictionary<string, EditorAction> _byId = [];
    private readonly EventDispatcher _events;

    public ActionRegistry(EventDispatcher events) => _events = events;

    /// <summary>Every registered action, in registration order (the keymap and the keybindings page
    /// present them in this order).</summary>
    public IReadOnlyList<EditorAction> All
    {
        get
        {
            lock (_sync)
                return [.. _actions];
        }
    }

    public EditorAction? Find(string id)
    {
        lock (_sync)
            return _byId.GetValueOrDefault(id);
    }

    /// <summary>Registers (or replaces) an action's metadata.</summary>
    public void Register(EditorAction action)
    {
        lock (_sync)
        {
            if (_byId.TryGetValue(action.Id, out var existing))
                _actions[_actions.IndexOf(existing)] = action;
            else
                _actions.Add(action);
            _byId[action.Id] = action;
        }

        _events.Dispatch(new ActionsChanged());
    }

    /// <summary>Publishes the action's <see cref="EditorActionInvoked"/> — how menus (and anything
    /// else action-shaped) run an action without knowing its executor.</summary>
    public void Invoke(string id) => _events.Dispatch(new EditorActionInvoked(id));
}
