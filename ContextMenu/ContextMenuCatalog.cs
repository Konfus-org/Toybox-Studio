using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Toybox.Studio.ContextMenu;

/// <summary>
/// Discovers every concrete <see cref="ContextMenu{T}"/> in the Studio assembly, registers it in DI, and routes
/// a right-clicked object to the matching menu by type. It is the only menu type <see cref="MenuOpenBehavior"/>
/// talks to and it references no domain — the menus self-describe their target via their type parameter.
/// Registration mirrors <c>DockableCatalog</c>: <see cref="Register"/> runs at composition time, the instance is
/// resolved from the container, and it is published once to <see cref="Current"/> so the static behaviour can
/// reach it.
/// </summary>
public sealed class ContextMenuCatalog
{
    private readonly IReadOnlyList<IContextMenu> _menus;

    public ContextMenuCatalog(IEnumerable<IContextMenu> menus) => _menus = menus.ToList();

    /// <summary>The app-wide instance, set once at startup (see <c>App.StartupAsync</c>).</summary>
    public static ContextMenuCatalog? Current { get; set; }

    /// <summary>
    /// Scans for <see cref="ContextMenu{T}"/> subclasses and registers each one (concretely — so a menu exposing
    /// extra surface, like the entity menu's rename signal, can be injected directly — and as
    /// <see cref="IContextMenu"/> for the catalog to discover), then the catalog itself. Call from
    /// <c>ConfigureServices</c>.
    /// </summary>
    public static void Register(IServiceCollection services)
    {
        foreach (var type in Scan())
        {
            services.TryAddSingleton(type);
            services.AddSingleton<IContextMenu>(sp => (IContextMenu)sp.GetRequiredService(type));
        }

        services.AddSingleton<ContextMenuCatalog>();
    }

    /// <summary>Builds the menu registered for <paramref name="target"/>'s type, or null when there is none /
    /// nothing to show.</summary>
    public Task<SearchableMenuViewModel?> BuildAsync(object target) =>
        Resolve(target.GetType()) is { } menu
            ? menu.BuildAsync(target)
            : Task.FromResult<SearchableMenuViewModel?>(null);

    /// <summary>Whether any menu is registered for <paramref name="target"/>'s type (without building it).</summary>
    public bool Handles(object target) => Resolve(target.GetType()) is not null;

    // Concrete context menus: a class deriving from ContextMenu<T> is its own unambiguous marker, so no
    // attribute is needed (the abstract base and the open generic are excluded by IsAbstract).
    private static IEnumerable<Type> Scan() =>
        typeof(ContextMenuCatalog).Assembly.GetTypes()
            .Where(type => type is { IsAbstract: false, IsClass: true }
                           && typeof(IContextMenu).IsAssignableFrom(type));

    // The registered menu whose Target type fits the clicked object: an exact match wins, then a concrete
    // (class) target over an interface one, so a view-model's own menu beats a shared-interface menu it also
    // qualifies for.
    private IContextMenu? Resolve(Type targetType) =>
        _menus.FirstOrDefault(menu => menu.Target == targetType)
        ?? _menus
            .Where(menu => menu.Target.IsAssignableFrom(targetType))
            .OrderBy(menu => menu.Target.IsInterface ? 1 : 0)
            .FirstOrDefault();
}
