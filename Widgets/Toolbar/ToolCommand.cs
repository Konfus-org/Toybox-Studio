using System.Collections.Generic;

namespace Toybox.Studio.Widgets.Toolbar;

/// <summary>
/// A tool's command: an ordered list of steps run in sequence, stopping at the first failed (awaited) step.
/// </summary>
public sealed class ToolCommand
{
    public List<ToolCommandStep> Steps { get; set; } = [];
}
