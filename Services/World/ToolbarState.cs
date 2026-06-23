using System;
using System.Collections.Generic;

namespace Toybox.Studio.Services.World;

/// <summary>
/// The generic "which radio member is active per group" store the data-driven toolbar binds its toggle state
/// to. A grouped tool shows checked when its <c>ActiveStateKey</c> equals its group's active key. The gizmo
/// group is the first consumer, bridged from <see cref="GizmoTool"/> by <see cref="GizmoToolbarBridge"/>.
/// </summary>
public sealed class ToolbarState
{
    private readonly Dictionary<string, string> _activeByGroup = [];

    /// <summary>Raised with the group key whenever a group's active member changes.</summary>
    public event Action<string>? GroupChanged;

    /// <summary>The active state key for a group, or null if none has been set.</summary>
    public string? GetActive(string group) => _activeByGroup.GetValueOrDefault(group);

    /// <summary>Sets the active state key for a group, raising <see cref="GroupChanged"/> if it changed.</summary>
    public void SetActive(string group, string key)
    {
        if (_activeByGroup.TryGetValue(group, out var existing) && existing == key)
            return;

        _activeByGroup[group] = key;
        GroupChanged?.Invoke(group);
    }
}
