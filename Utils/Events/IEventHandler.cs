namespace Toybox.Studio.Events;

/// <summary>
/// Handles one typed event struct dispatched through the <see cref="EventDispatcher"/>. A class handles
/// several event types by implementing this once per type. Handlers never know (or care) who dispatched
/// the event — only that it happened; they marshal to the UI thread themselves when needed, since events
/// are dispatched on whatever thread raised them.
/// </summary>
public interface IEventHandler<TEvent> where TEvent : struct
{
    void Handle(in TEvent evt);
}
