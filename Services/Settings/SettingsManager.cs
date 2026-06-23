using Toybox.Studio.Utils;

namespace Toybox.Studio.Services.Settings;

/// <summary>
/// The single owner of the editor's persisted settings. Loads <see cref="EditorSettings"/> once at startup and
/// exposes the live instance via <see cref="Settings"/>; every consumer reads through here rather than holding
/// its own copy. Persisting goes through <see cref="SaveAsync"/> (or <see cref="NotifyChanged"/> for an in-place
/// edit), which raises <see cref="Changed"/> so any area can react to a settings change without polling — see
/// <see cref="IListenable"/> and <see cref="ListenableExtensions.Listen"/>.
/// </summary>
public sealed class SettingsManager : IListenable
{
    /// <inheritdoc/>
    public event Action? Changed;

    /// <summary>The live editor settings instance. Stable for the app's lifetime; mutate then persist via this manager.</summary>
    public EditorSettings Settings { get; } = EditorSettings.Load();

    /// <summary>
    /// Writes the current settings to disk (off the UI thread) and then notifies listeners. Callers either
    /// <c>await</c> this (the Settings panel) or fire-and-forget it (theme pick, engine path, recent-project list).
    /// </summary>
    public async Task SaveAsync()
    {
        await Settings.SaveAsync().ConfigureAwait(false);
        NotifyChanged();
    }

    /// <summary>
    /// Broadcasts that the settings changed without writing to disk — for re-applying the already-saved values
    /// after an in-place edit or a cancelled buffer revert.
    /// </summary>
    public void NotifyChanged() => Changed?.Invoke();
}
