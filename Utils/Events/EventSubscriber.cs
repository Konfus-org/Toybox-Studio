namespace Toybox.Studio.Events;

/// <summary>
/// Base for a class whose event handlers register themselves: constructing it registers every
/// <see cref="IEventHandler{TEvent}"/> the subclass implements with the dispatcher, and disposing
/// unregisters them — no subscriber ever calls RegisterAll/UnregisterAll by hand. Subclasses dispatch
/// their own events through <see cref="Events"/>. Handlers are live from the base constructor on, so a
/// subscriber must be constructed before events start flowing (the composition root wires everything
/// up front, so this holds by construction).
/// </summary>
public abstract class EventSubscriber : IDisposable
{
    protected EventSubscriber(EventDispatcher events)
    {
        Events = events;
        events.RegisterAll(this);
    }

    /// <summary>The shared dispatcher this subscriber's handlers are registered with (and that
    /// subclasses dispatch their own event structs through).</summary>
    protected EventDispatcher Events { get; }

    public virtual void Dispose()
    {
        Events.UnregisterAll(this);
        GC.SuppressFinalize(this);
    }
}
