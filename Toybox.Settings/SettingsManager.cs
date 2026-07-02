using Toybox.Studio.Utils;

namespace Toybox.Studio.Settings;

/// <summary>
/// The single owner of the editor's persisted settings: the editor's own <see cref="EditorSettings"/> load
/// once at startup and live for the app's lifetime. Every consumer reads through here rather than holding its
/// own copy. Persisting goes through <see cref="SaveAsync"/>, or <see cref="NotifyChanged"/> for an in-place
/// edit — each raises <see cref="Changed"/> so any area can react without polling (see <see cref="IListenable"/>
/// and <see cref="ListenableExtensions.Listen"/>).
///
/// The open project's settings asset (its AppSettings.json) is owned separately by
/// <c>ProjectSettingsService</c> in the asset layer, since loading it depends on the asset system.
/// </summary>
public sealed class SettingsManager : IListenable
{
    /// <inheritdoc/>
    public event Action? Changed;

    /// <summary>The live editor settings instance. Stable for the app's lifetime; mutate then persist via this manager.</summary>
    public EditorSettings Settings { get; } = EditorSettings.Load();

    /// <summary>
    /// Writes the current editor settings to disk (off the UI thread) and then notifies listeners. Callers either
    /// <c>await</c> this (the Settings panel) or fire-and-forget it (theme pick, engine path, recent-project list).
    /// </summary>
    public async Task SaveAsync()
    {
        await Settings.SaveAsync().ContinueOnAnyContext();
        NotifyChanged();
    }

    /// <summary>
    /// Broadcasts that the settings changed without writing to disk — for re-applying the already-saved values
    /// after an in-place edit or a cancelled buffer revert. Always raises <see cref="Changed"/> on the UI thread
    /// (marshalling if called from a background thread, e.g. after a disk save), since listeners mutate Avalonia
    /// resources and view-model state directly.
    /// </summary>
    public void NotifyChanged() => Dispatch.To(DispatchContext.UI, () => Changed?.Invoke());
}
