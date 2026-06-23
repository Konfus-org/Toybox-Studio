using Avalonia.Controls;
using Toybox.Studio.Shell.Workspace;

namespace Toybox.Studio.Widgets.ScriptEditor;

[Dockable("ScriptEditor", Title = "Script editor", Icon = "Code", Slot = DockSlot.Float,
    Width = 900, Height = 620, Order = 0)]
public partial class ScriptEditorView : UserControl
{
    public ScriptEditorView()
    {
        InitializeComponent();
    }
}
