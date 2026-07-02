using Avalonia.Controls;
using Toybox.Studio.Shell.Workspace;

namespace Toybox.Studio.ScriptEditor;

[Dockable(Title = "Script editor", Icon = Icon.Code, IconColor = Toybox.Studio.Utils.PaletteColor.Blue, Slot = DockSlot.Float,
    Width = 900, Height = 620, Order = 0)]
public partial class ScriptEditorView : UserControl
{
    public ScriptEditorView()
    {
        InitializeComponent();
    }
}
