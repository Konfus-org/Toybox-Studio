using Avalonia.Threading;

namespace Toybox.Studio;

/// <summary>
/// Marshals work onto a chosen <see cref="DispatchContext"/>. Replaces scattered
/// <c>Dispatcher.UIThread.Post(...)</c> calls with an intent-revealing
/// <c>Dispatch.To(DispatchContext.UI, ...)</c> at the call site.
/// </summary>
public static class Dispatch
{
    /// <summary>
    /// Posts <paramref name="action"/> to run on the given context. UI work is queued onto the
    /// Avalonia dispatcher; background work is handed to the thread pool.
    /// </summary>
    public static void To(DispatchContext context, Action action)
    {
        switch (context)
        {
            case DispatchContext.UI:
                Dispatcher.UIThread.Post(action);
                break;
            case DispatchContext.Background:
                Task.Run(action).FireAndForget();
                break;
        }
    }

    /// <summary>
    /// Posts UI work at an explicit dispatcher priority. Only meaningful for
    /// <see cref="DispatchContext.UI"/>; other contexts ignore the priority.
    /// </summary>
    public static void To(DispatchContext context, Action action, DispatcherPriority priority)
    {
        if (context == DispatchContext.UI)
            Dispatcher.UIThread.Post(action, priority);
        else
            To(context, action);
    }
}
