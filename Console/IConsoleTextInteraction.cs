namespace Toybox.Studio.Console;

/// <summary>
/// The view-level text operations the console's text control exposes to its view-model, so a context menu
/// routed at the <see cref="ConsoleViewModel"/> can act on the live on-screen text. Selection is inherently a
/// view concern (it lives on the text control), so the control implements this and registers itself on
/// <see cref="ConsoleViewModel.Text"/> while attached; the view-model owns the clipboard step, copying the
/// text this hands back.
/// </summary>
public interface IConsoleTextInteraction
{
    /// <summary>Whether a non-empty range of text is currently selected.</summary>
    bool HasSelection { get; }

    /// <summary>The text to copy: the current selection, or the whole (visible) console when nothing is selected.</summary>
    string CopyText();

    /// <summary>Selects every line.</summary>
    void SelectAll();
}
