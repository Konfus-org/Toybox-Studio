using Avalonia.Markup.Xaml;
using Avalonia;

namespace Toybox.Studio;

/// <summary>
/// The Avalonia application: the styles and theme resources every window draws from. It holds no
/// startup logic — the <c>Launcher</c> (the composition root) boots this app and runs the launch flow.
/// </summary>
public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
}
