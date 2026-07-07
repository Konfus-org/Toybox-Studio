namespace Toybox.Studio.Settings;

// Every event struct the settings domain dispatches, in one place. Handlers implement IEventHandler<T>
// and register with the shared EventDispatcher; nobody references the publisher. Events fire on
// whatever thread raised them — handlers marshal themselves.

/// <summary>The <see cref="SettingsManager"/> swapped the open project's app settings mirror (loaded,
/// reloaded, or dropped on disconnect), carrying the mirror it swapped to — null when dropped.</summary>
public readonly record struct AppSettingsChanged(AppSettings? App);

/// <summary>The editor settings changed, dispatched per <see cref="SettingsManager.SaveAsync"/> — the
/// commit point of the mutate-then-save editing flow — carrying the live (already mutated) graph.</summary>
public readonly record struct EditorSettingsChanged(EditorSettings Editor);
