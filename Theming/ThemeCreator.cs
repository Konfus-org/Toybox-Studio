using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Toybox.Studio.Shell;

namespace Toybox.Studio.Theming;

/// <summary>
/// Opens the modal Theme Creator over the main window. Lets a view-model launch it as a command without
/// holding a <c>Window</c> reference or routing an event back to its view.
/// </summary>
public sealed class ThemeCreator(ThemeManager themes)
{
    public async Task CreateAsync()
    {
        if ((Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
            ?.MainWindow is not { } owner)
            return;

        await ThemeCreatorWindow.ShowAsync(owner, themes).ContinueOnAnyContext();
    }
}
