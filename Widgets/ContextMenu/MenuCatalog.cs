using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Toybox.Studio.Services.Logging;
using Toybox.Studio.Services.Settings;

namespace Toybox.Studio.Widgets.ContextMenu;

/// <summary>
/// Resolves a context menu by id: the built-in <see cref="MenuCatalogDefaults"/> with any user overrides merged
/// over them. A user can drop <c>&lt;menuId&gt;.json</c> files in <c>~/.toybox/ContextMenus</c> to add items or
/// replace a built-in item (matched by its <see cref="MenuEntry.Id"/>) — the same data-driven, user-configurable
/// model as the toolbars. The catalog is built once at construction; the files are read then.
/// </summary>
public sealed class MenuCatalog
{
    private static readonly string OverrideDirectory =
        Path.Combine(EditorSettings.BaseDirectory, "ContextMenus");

    private readonly Dictionary<string, MenuDefinition> _menus = new(StringComparer.Ordinal);

    public MenuCatalog(Logger log)
    {
        foreach (var definition in MenuCatalogDefaults.All())
            _menus[definition.Id] = definition;

        MergeUserOverrides(log);
    }

    /// <summary>The merged menu for <paramref name="id"/>, or null when no such menu exists.</summary>
    public MenuDefinition? Resolve(string id) => _menus.GetValueOrDefault(id);

    private void MergeUserOverrides(Logger log)
    {
        if (!Directory.Exists(OverrideDirectory))
            return;

        foreach (var path in Directory.EnumerateFiles(OverrideDirectory, "*.json"))
        {
            try
            {
                if (JsonConvert.DeserializeObject<MenuDefinition>(File.ReadAllText(path)) is not { } user
                    || string.IsNullOrEmpty(user.Id))
                    continue;

                _menus[user.Id] = _menus.TryGetValue(user.Id, out var builtIn)
                    ? Merge(builtIn, user)
                    : user;
            }
            catch (Exception exception)
            {
                log.Warning($"Ignoring malformed context-menu override '{Path.GetFileName(path)}': {exception.Message}");
            }
        }
    }

    // User items replace a built-in item with the same id (in place) and otherwise append to the end, so a
    // user can retune one row without redeclaring the whole menu.
    private static MenuDefinition Merge(MenuDefinition builtIn, MenuDefinition user)
    {
        var items = builtIn.Items.ToList();
        foreach (var item in user.Items)
        {
            var existing = items.FindIndex(candidate => candidate.Id == item.Id && item.Id.Length > 0);
            if (existing >= 0)
                items[existing] = item;
            else
                items.Add(item);
        }

        return new MenuDefinition { Id = builtIn.Id, Items = items };
    }
}
