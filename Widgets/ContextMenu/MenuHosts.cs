namespace Toybox.Studio.Widgets.ContextMenu;

/// <summary>
/// The stable host ids for the editor's context menus. Each doubles as the favorites scope for its rows and
/// matches the <c>cm:MenuOpenBehavior.Menu="…"</c> literal set on the views (world tree, viewport, inspector,
/// property grid).
/// </summary>
public static class MenuHosts
{
    public const string Entity = "entity";

    public const string Background = "background";

    public const string Component = "inspector.component";

    public const string Property = "property";
}
