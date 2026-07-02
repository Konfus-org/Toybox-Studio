namespace Toybox.Studio.Utils;

/// <summary>
/// A context that work can be marshaled onto via <see cref="Dispatch.To(DispatchContext, System.Action)"/>.
/// </summary>
public enum DispatchContext
{
    /// <summary>
    /// The Avalonia UI thread, where view models and controls must be touched.
    /// </summary>
    UI,

    /// <summary>
    /// The thread pool, for work that must not run on the UI thread.
    /// </summary>
    Background,
}
