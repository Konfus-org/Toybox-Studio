namespace Toybox.Studio.Widgets.ContextMenu;

/// <summary>
/// When a <see cref="MenuEntry"/> is shown, gated against what the menu was opened over (its
/// <see cref="Services.Commands.MenuContext"/>). Evaluated synchronously by the menu view-model as the menu
/// opens; clipboard-dependent gating is intentionally absent (paste is always offered and no-ops on an empty
/// clipboard), so visibility never has to await the OS clipboard.
/// </summary>
public enum MenuCondition
{
    /// <summary>Always shown.</summary>
    Always,

    /// <summary>Shown only when at least one entity is selected (an item menu, not a background menu).</summary>
    Entity,

    /// <summary>Shown only when exactly one entity is selected.</summary>
    SingleEntity,

    /// <summary>Shown only when more than one entity is selected.</summary>
    MultiEntity,

    /// <summary>Shown only when the menu targets a component (an inspector component header).</summary>
    Component,

    /// <summary>Shown only when the menu was opened over empty space (a background menu).</summary>
    Background,
}
