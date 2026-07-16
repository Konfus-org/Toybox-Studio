namespace Toybox.Studio.Utils;

/// <summary>
/// An <see cref="IProgress{T}"/> that hands each reported value straight to a delegate on the thread that
/// reported it, with no <see cref="System.Threading.SynchronizationContext"/> hop (unlike the BCL's
/// <see cref="System.Progress{T}"/>). Used where the consumer marshals to its own thread itself — e.g. a
/// view-model that dispatches the update to the UI — so the report shouldn't be posted anywhere in between.
/// </summary>
public sealed class DelegateProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
