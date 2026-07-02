namespace Toybox.Studio.Events;

/// <summary>
/// The app-wide event bus: dispatches typed event structs to every registered
/// <see cref="IEventHandler{TEvent}"/> for that type. This replaces C# events/delegates for domain
/// signals so publishers and subscribers never reference each other — a publisher just dispatches a
/// struct, and whoever cares handles it. One instance, created by the composition root and handed to
/// both sides. Thread-safe: events dispatch synchronously on the calling thread, so handlers marshal
/// to the UI themselves when needed.
/// </summary>
public sealed class EventDispatcher
{
    private readonly object _sync = new();
    private readonly Dictionary<Type, List<object>> _handlers = [];

    public void Register<TEvent>(IEventHandler<TEvent> handler) where TEvent : struct =>
        Add(typeof(TEvent), handler);

    public void Unregister<TEvent>(IEventHandler<TEvent> handler) where TEvent : struct =>
        Remove(typeof(TEvent), handler);

    /// <summary>
    /// Registers <paramref name="handler"/> for every event type it implements
    /// <see cref="IEventHandler{TEvent}"/> for. Subscribers don't call this by hand — deriving
    /// <see cref="EventSubscriber"/> / <see cref="ObservableEventSubscriber"/> (or another base that
    /// does, like an owned app) registers them automatically.
    /// </summary>
    public void RegisterAll(object handler)
    {
        foreach (var eventType in HandledEventTypes(handler))
            Add(eventType, handler);
    }

    /// <summary>Unregisters <paramref name="handler"/> from every event type it handles.</summary>
    public void UnregisterAll(object handler)
    {
        foreach (var eventType in HandledEventTypes(handler))
            Remove(eventType, handler);
    }

    /// <summary>
    /// Dispatches an event to every handler registered for its type, synchronously on this thread.
    /// An event nobody handles is simply dropped.
    /// </summary>
    public void Dispatch<TEvent>(in TEvent evt) where TEvent : struct
    {
        object[] snapshot;
        lock (_sync)
        {
            if (!_handlers.TryGetValue(typeof(TEvent), out var handlers))
                return;

            snapshot = [.. handlers];
        }

        foreach (var handler in snapshot)
            ((IEventHandler<TEvent>)handler).Handle(in evt);
    }

    private void Add(Type eventType, object handler)
    {
        lock (_sync)
        {
            if (!_handlers.TryGetValue(eventType, out var handlers))
                _handlers[eventType] = handlers = [];

            if (!handlers.Contains(handler))
                handlers.Add(handler);
        }
    }

    private void Remove(Type eventType, object handler)
    {
        lock (_sync)
        {
            if (_handlers.TryGetValue(eventType, out var handlers))
                handlers.Remove(handler);
        }
    }

    private static IEnumerable<Type> HandledEventTypes(object handler) =>
        handler.GetType().GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEventHandler<>))
            .Select(i => i.GenericTypeArguments[0]);
}
