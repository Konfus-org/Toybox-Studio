using System;

namespace Toybox.Studio.Services.World;

/// <summary>
/// The editor's current viewport transform tool, shared between the viewport toolbar (which sets it) and
/// the viewports (which gate box-select on it and reflect it in their toolbar). Pushed to the engine by
/// <see cref="GizmoSync"/>.
/// </summary>
public sealed class GizmoTool
{
    private GizmoMode _mode = GizmoMode.None;

    public event Action? Changed;

    public GizmoMode Mode
    {
        get => _mode;
        set
        {
            if (_mode == value)
                return;
            _mode = value;
            Changed?.Invoke();
        }
    }
}
