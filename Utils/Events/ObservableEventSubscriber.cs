using CommunityToolkit.Mvvm.ComponentModel;

namespace Toybox.Studio.Events;

/// <summary>
/// An <see cref="ObservableObject"/> whose event handlers register themselves — the base for a
/// view-model that consumes dispatched events. Same contract as <see cref="EventSubscriber"/> (which
/// C#'s single inheritance keeps it from also deriving): every <see cref="IEventHandler{TEvent}"/> the
/// subclass implements is registered on construction and unregistered on dispose.
/// </summary>
public abstract class ObservableEventSubscriber : ObservableObject, IDisposable
{
    protected ObservableEventSubscriber(EventDispatcher events)
    {
        Events = events;
        events.RegisterAll(this);
    }

    /// <summary>The shared dispatcher this subscriber's handlers are registered with.</summary>
    protected EventDispatcher Events { get; }

    public virtual void Dispose()
    {
        Events.UnregisterAll(this);
        GC.SuppressFinalize(this);
    }
}
