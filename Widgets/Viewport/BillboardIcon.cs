namespace Toybox.Studio.Widgets.Viewport;

/// <summary>
/// One icon in an entity's viewport billboard stack: the Lucide icon name and tbx Color constant a component
/// advertises through its <c>[[tbx::viewport_icon]]</c> attribute (resolved by <c>IconView</c> for display).
/// </summary>
public sealed record BillboardIcon(string Name, string? Color);
