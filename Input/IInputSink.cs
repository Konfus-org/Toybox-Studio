namespace Toybox.Studio.Input;

/// <summary>
/// Anything captured input can be forwarded to — typically a view that finishes the snapshot (e.g.
/// mapping the pointer into its content image) before it flows onward. The
/// <see cref="InputBindingBehavior"/> binds one of these, so the capture layer never knows what sits
/// behind it.
/// </summary>
public interface IInputSink
{
    void ForwardInput(InputSnapshot input);
}
