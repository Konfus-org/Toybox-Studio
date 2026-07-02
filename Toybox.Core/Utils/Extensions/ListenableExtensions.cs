namespace Toybox.Studio.Utils.Extensions;

/// <summary>
/// Subscription helpers for <see cref="IListenable"/> holders.
/// </summary>
public static class ListenableExtensions
{
    /// <summary>
    /// Subscribes <paramref name="onChanged"/> to <paramref name="source"/>'s <see cref="IListenable.Changed"/>
    /// event and, when <paramref name="fireImmediately"/> is true (the default), invokes it once now — the common
    /// "react to the current value and to every future change" pattern. Returns an <see cref="IDisposable"/> that
    /// unsubscribes; app-lifetime listeners can ignore it.
    /// </summary>
    public static IDisposable Listen(this IListenable source, Action onChanged, bool fireImmediately = true)
    {
        source.Changed += onChanged;
        if (fireImmediately)
            onChanged();

        return new Unsubscriber(source, onChanged);
    }

    private sealed class Unsubscriber(IListenable source, Action onChanged) : IDisposable
    {
        public void Dispose() => source.Changed -= onChanged;
    }
}
