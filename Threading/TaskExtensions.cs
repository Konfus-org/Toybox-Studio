using System.Runtime.CompilerServices;

namespace Toybox.Studio;

/// <summary>
/// Intent-revealing wrappers over <see cref="Task.ConfigureAwait(bool)"/> plus a safe fire-and-forget
/// helper. Prefer <see cref="ContinueOnAnyContext(Task)"/> for any await that does not need to resume on
/// the captured (UI) context — which is most of them — and reserve <see cref="ContinueOnSameContext(Task)"/>
/// for the few continuations that must run back on the original context.
/// </summary>
public static class TaskExtensions
{
    // The sink used by FireAndForget when no explicit onError is passed. Wired once at startup (the
    // Logger installs itself), so a forgotten task's failure lands in the unified log instead of being
    // silently swallowed. Null until wired, which only happens before logging exists.
    private static Action<Exception>? _defaultErrorHandler;

    /// <summary>
    /// Installs the handler that <see cref="FireAndForget(Task, Action{Exception})"/> calls when no
    /// explicit <c>onError</c> is given. The app's logger wires this so unobserved background failures
    /// are logged automatically.
    /// </summary>
    public static void SetDefaultErrorHandler(Action<Exception> handler) => _defaultErrorHandler = handler;

    /// <summary>
    /// Awaits without capturing the current synchronization context: the continuation may resume on any
    /// thread. The default for service/background work. Equivalent to <c>ConfigureAwait(false)</c>.
    /// </summary>
    public static ConfiguredTaskAwaitable ContinueOnAnyContext(this Task task) =>
        task.ConfigureAwait(false);

    /// <inheritdoc cref="ContinueOnAnyContext(Task)"/>
    public static ConfiguredTaskAwaitable<T> ContinueOnAnyContext<T>(this Task<T> task) =>
        task.ConfigureAwait(false);

    /// <inheritdoc cref="ContinueOnAnyContext(Task)"/>
    public static ConfiguredValueTaskAwaitable ContinueOnAnyContext(this ValueTask task) =>
        task.ConfigureAwait(false);

    /// <inheritdoc cref="ContinueOnAnyContext(Task)"/>
    public static ConfiguredValueTaskAwaitable<T> ContinueOnAnyContext<T>(this ValueTask<T> task) =>
        task.ConfigureAwait(false);

    /// <summary>
    /// Awaits while capturing the current synchronization context: the continuation resumes on the
    /// original (e.g. UI) context. Use only when the code after the await must run there — touching view
    /// models, controls, or showing dialogs. Equivalent to <c>ConfigureAwait(true)</c>.
    /// </summary>
    public static ConfiguredTaskAwaitable ContinueOnSameContext(this Task task) =>
        task.ConfigureAwait(true);

    /// <inheritdoc cref="ContinueOnSameContext(Task)"/>
    public static ConfiguredTaskAwaitable<T> ContinueOnSameContext<T>(this Task<T> task) =>
        task.ConfigureAwait(true);

    /// <inheritdoc cref="ContinueOnSameContext(Task)"/>
    public static ConfiguredValueTaskAwaitable ContinueOnSameContext(this ValueTask task) =>
        task.ConfigureAwait(true);

    /// <inheritdoc cref="ContinueOnSameContext(Task)"/>
    public static ConfiguredValueTaskAwaitable<T> ContinueOnSameContext<T>(this ValueTask<T> task) =>
        task.ConfigureAwait(true);

    /// <summary>
    /// Runs a task without awaiting it, deliberately not observing completion on the calling path.
    /// Exceptions are routed to <paramref name="onError"/> when given, otherwise to the default handler
    /// (the app logger, see <see cref="SetDefaultErrorHandler"/>), so a forgotten task can never fault
    /// the process with an unobserved exception. Makes an intentional "don't await this" explicit instead
    /// of assigning to a discard.
    /// </summary>
    public static async void FireAndForget(this Task task, Action<Exception>? onError = null)
    {
        try
        {
            await task.ContinueOnAnyContext();
        }
        catch (Exception exception)
        {
            (onError ?? _defaultErrorHandler)?.Invoke(exception);
        }
    }

    /// <inheritdoc cref="FireAndForget(Task, Action{Exception})"/>
    public static async void FireAndForget(this ValueTask task, Action<Exception>? onError = null)
    {
        try
        {
            await task.ContinueOnAnyContext();
        }
        catch (Exception exception)
        {
            (onError ?? _defaultErrorHandler)?.Invoke(exception);
        }
    }
}
