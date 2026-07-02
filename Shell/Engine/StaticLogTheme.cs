using Toybox.Studio.Logging;

namespace Toybox.Studio.Shell;

/// <summary>
/// Fixed log colours for the engine console (the theming system that used to supply these is gone).
/// <see cref="Changed"/> never fires; the colours are pushed once per connection.
/// </summary>
public sealed class StaticLogTheme : ILogTheme
{
    // Never raised — the colours are static — but required by the interface.
    public event Action? Changed { add { } remove { } }

    public (string Info, string Warning, string Error) Colors => ("#9CD3FF", "#FFD37E", "#FF7A7A");
}
