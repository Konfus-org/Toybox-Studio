namespace Toybox.Studio.Favorites;

// The event the favorites domain dispatches. Handlers implement IEventHandler<FavoritesChanged> and
// register with the shared EventDispatcher; nobody references the publisher. Fires on whatever thread
// raised the toggle — handlers marshal to the UI themselves.

/// <summary>A host's starred set changed (an item was starred or un-starred), carrying the host so a
/// listener can ignore changes to surfaces other than its own.</summary>
public readonly record struct FavoritesChanged(string Host);
