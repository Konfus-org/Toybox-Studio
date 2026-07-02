namespace Toybox.Studio.Viewport;

/// <summary>
/// One icon in an entity's viewport billboard stack: the Lucide icon and palette colour a component
/// advertises through its <c>[[tbx::viewport_icon]]</c> attribute (resolved by <c>IconView</c> for display).
/// </summary>
public sealed record BillboardIcon(Icon Name, Avalonia.Media.Color? Color);
