namespace Toybox.Studio.AppHosting;

// Every event struct the generic owned-app infrastructure dispatches, in one place. Handlers implement
// IEventHandler<T> and register with the shared EventDispatcher; nobody references the publisher.
// All events fire on background threads — handlers marshal to the UI themselves.

/// <summary>The hosted session's connection lifecycle changed, with whether the host owns or attached
/// to the app's process.</summary>
public readonly record struct ConnectionChanged(ConnectionState State, HostKind Kind);

/// <summary>The connection to the owned app dropped, for any reason.</summary>
public readonly record struct AppDisconnected;

/// <summary>The owned owned-app process terminated, for any reason.</summary>
public readonly record struct AppExited(int ExitCode);

/// <summary>An already-running instance of a owned app answered on its well-known port (probed while the
/// app was not connected). Dispatched for each sighting, so a failed attach is naturally retried on the
/// next probe.</summary>
public readonly record struct AppInstanceDetected(int Port);

/// <summary>The watchdog measured a ping round-trip to the connected owned app.</summary>
public readonly record struct AppPinged(TimeSpan RoundTrip);

/// <summary>A still-connected owned app stopped answering pings — it has been silent for
/// <see cref="Silence"/>, past the watchdog's threshold, and appears frozen.</summary>
public readonly record struct AppStoppedResponding(TimeSpan Silence);

/// <summary>A previously unresponsive owned app resumed responding (or its connection went away) —
/// dismiss any "not responding" prompt.</summary>
public readonly record struct AppResumedResponding;
