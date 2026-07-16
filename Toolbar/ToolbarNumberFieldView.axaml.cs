using Avalonia.Controls;

namespace Toybox.Studio.Toolbar;

/// <summary>
/// The editable snap-amount chip a toolbar surfaces after its tools. See <see cref="ToolbarNumberField"/>;
/// the drag-to-scrub gesture on the spinner is <c>NumberScrubBehavior</c> (the same one the property
/// grid's number fields use).
/// </summary>
public partial class ToolbarNumberFieldView : UserControl
{
    public ToolbarNumberFieldView()
    {
        InitializeComponent();
    }
}
