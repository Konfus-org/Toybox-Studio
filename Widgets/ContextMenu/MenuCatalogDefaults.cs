using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Toybox.Studio.Services.Rpc;
using Toybox.Studio.Widgets.Toolbar;

namespace Toybox.Studio.Widgets.ContextMenu;

/// <summary>
/// The built-in context menus, authored as data (the same way <c>ToolbarLayout.Default()</c> authors the
/// built-in toolbars). Each entity/component verb runs an <c>editor.*</c> command that
/// <see cref="Services.Commands.EditorCommands"/> resolves against the selection and the menu's context. These
/// are the defaults <see cref="MenuCatalog"/> serves before any user JSON override is merged in.
/// </summary>
public static class MenuCatalogDefaults
{
    /// <summary>The host id for the entity menu shared by the world tree, the inspector header and the viewport.</summary>
    public const string EntityMenu = "entity";

    /// <summary>The host id for the world-tree / viewport empty-space (background) menu.</summary>
    public const string BackgroundMenu = "background";

    /// <summary>The host id for the inspector's per-component menu.</summary>
    public const string ComponentMenu = "inspector.component";

    /// <summary>The host id for the application toolbar's favorites/quick-action menu (= the toolbar favorites bucket).</summary>
    public const string ToolbarMenu = "toolbar";

    /// <summary>The host id for a property-grid row's menu (copy/paste value, reset). Actions are bound locally.</summary>
    public const string PropertyMenu = "property";

    public static IReadOnlyList<MenuDefinition> All() =>
    [
        Entity(),
        Background(),
        Component(),
        Toolbar(),
        Property(),
    ];

    private static MenuDefinition Entity() => new()
    {
        Id = EntityMenu,
        Items =
        [
            Item("entity.rename", "Rename", "Pencil", "editor.entity.rename", when: MenuCondition.SingleEntity,
                gesture: "F2"),
            Separator(),
            Item("entity.cut", "Cut", "Scissors", "editor.clipboard.cut", gesture: "Ctrl+X"),
            Item("entity.copy", "Copy", "Copy", "editor.clipboard.copy", gesture: "Ctrl+C"),
            Item("entity.paste", "Paste", "ClipboardPaste", "editor.clipboard.paste", gesture: "Ctrl+V"),
            Item("entity.duplicate", "Duplicate", "CopyPlus", "editor.entity.duplicate", gesture: "Ctrl+D"),
            Separator(),
            Item("entity.moveUp", "Move Up", "ArrowUp", "editor.entity.moveUp",
                when: MenuCondition.SingleEntity),
            Item("entity.moveDown", "Move Down", "ArrowDown", "editor.entity.moveDown",
                when: MenuCondition.SingleEntity),
            Separator(),
            Item("entity.makeGlobal", "Make Global", "Globe", "editor.entity.setGlobal",
                @params: new JObject { ["global"] = true }),
            Item("entity.makeStreamed", "Make Streamed", "Layers", "editor.entity.setGlobal",
                @params: new JObject { ["global"] = false }),
            Item("entity.toggleEnabled", "Enable / Disable", "Power", "editor.entity.toggleEnabled",
                when: MenuCondition.SingleEntity),
            Separator(),
            Item("entity.delete", "Delete", "Trash2", "editor.entity.delete", iconColor: "RED",
                gesture: "Del"),
        ],
    };

    private static MenuDefinition Background() => new()
    {
        Id = BackgroundMenu,
        Items =
        [
            Item("background.addEntity", "Add Entity", "Plus", "editor.entity.add", iconColor: "GREEN"),
            Item("background.addGlobal", "Add Global Entity", "Globe", "editor.entity.add", iconColor: "GREEN",
                @params: new JObject { ["global"] = true }),
            Separator(),
            Item("background.paste", "Paste", "ClipboardPaste", "editor.clipboard.paste", gesture: "Ctrl+V"),
        ],
    };

    private static MenuDefinition Component() => new()
    {
        Id = ComponentMenu,
        Items =
        [
            Item("component.copy", "Copy Component", "Copy", "editor.component.copy"),
            Item("component.paste", "Paste Component", "ClipboardPaste", "editor.component.paste"),
            Separator(),
            Item("component.remove", "Remove Component", "Trash2", "editor.component.remove", iconColor: "RED"),
        ],
    };

    // The application toolbar's quick-action menu: common app/world commands the user can run and star. Starred
    // ones pin to the Favorites group at the top, giving the top toolbar the same favoritable surface as the
    // context menus. Each runs through the shared command runner (an editor.* verb or a plain engine RPC).
    private static MenuDefinition Toolbar() => new()
    {
        Id = ToolbarMenu,
        Items =
        [
            Item("toolbar.save", "Save World", "Save", "world.save", gesture: "Ctrl+S"),
            Separator(),
            Item("toolbar.addEntity", "Add Entity", "Plus", "editor.entity.add", iconColor: "GREEN"),
            Item("toolbar.addGlobal", "Add Global Entity", "Globe", "editor.entity.add", iconColor: "GREEN",
                @params: new JObject { ["global"] = true }),
            Separator(),
            Item("toolbar.play", "Play", "Play", "editor.play", iconColor: "GREEN"),
            Item("toolbar.stop", "Stop", "Square", "editor.stop", iconColor: "RED"),
            Item("toolbar.pause", "Pause / Resume", "Pause", "editor.togglePause"),
        ],
    };

    // One property-grid row's menu: copy/paste its value and reset it to default. These have no command — the
    // actions are bound locally to the row's view-model by ContextMenuService.BuildProperty (the grid also
    // drives non-entity data like settings, so they can't be global engine verbs). Authored here anyway so the
    // labels/icons are user-overridable and the rows are favoritable like every other menu.
    private static MenuDefinition Property() => new()
    {
        Id = PropertyMenu,
        Items =
        [
            Local("property.copyValue", "Copy Value", "Copy", gesture: "Ctrl+C"),
            Local("property.pasteValue", "Paste Value", "ClipboardPaste", gesture: "Ctrl+V"),
            Separator(),
            Local("property.reset", "Reset to Default", "RotateCcw", iconColor: "YELLOW"),
        ],
    };

    // A command-less menu entry whose action is supplied at build time (see ContextMenuService.BuildProperty).
    private static MenuEntry Local(string id, string label, string icon, string? iconColor = null,
        string? gesture = null) => new()
    {
        Id = id,
        Label = label,
        Icon = icon,
        IconColor = iconColor,
        InputGesture = gesture,
    };

    private static MenuEntry Item(
        string id,
        string label,
        string icon,
        string method,
        string? iconColor = null,
        string? gesture = null,
        MenuCondition when = MenuCondition.Always,
        JObject? @params = null) => new()
    {
        Id = id,
        Label = label,
        Icon = icon,
        IconColor = iconColor,
        InputGesture = gesture,
        When = when,
        Command = new ToolCommand
        {
            Steps = [new ToolCommandStep { Kind = "rpc", Rpc = new RpcCall { Method = method, Params = @params } }],
        },
    };

    private static MenuEntry Separator() => new() { IsSeparator = true };
}
