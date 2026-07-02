using System;
using System.Threading.Tasks;
using Toybox.Studio.Favorites;
using Toybox.Studio.Utils;

namespace Toybox.Studio.ContextMenu;

/// <summary>
/// The base every context menu derives from: parameterise it with the right-clicked view-model
/// (<c>ContextMenu&lt;EntityViewModel&gt;</c>, or a shared interface like <see cref="IWorldMenuTarget"/>), inject
/// the services it needs through the constructor (as any other type does), and override <see cref="Build"/> to
/// add its rows with the fluent <see cref="MenuBuilder"/>. The type parameter alone declares what the menu is for
/// — <see cref="ContextMenuCatalog"/> discovers every concrete subclass and routes by <see cref="Target"/>, with
/// no attribute or DI/XAML wiring per menu. <see cref="Build"/> runs fresh each time the menu opens, with the
/// live <typeparamref name="T"/>, so it is plain async C# — loop, await, branch — and each row declares its own
/// show/enable condition.
/// </summary>
/// <typeparam name="T">The right-clicked view-model (or a shared interface) this menu serves.</typeparam>
public abstract class ContextMenu<T> : IContextMenu
    where T : class
{
    private readonly FavoritesManager _favorites;

    protected ContextMenu(FavoritesManager favorites) => _favorites = favorites;

    public Type Target => typeof(T);

    async Task<SearchableMenuViewModel?> IContextMenu.BuildAsync(object target)
    {
        if (target is not T typed)
            return null;

        var builder = new MenuBuilder();
        await Build(builder, typed).ContinueOnAnyContext();
        return builder.Compose(_favorites);
    }

    /// <summary>
    /// Adds the menu's rows for <paramref name="target"/>. Runs once per open on the live target; omit a row
    /// (<c>VisibleWhen(false)</c> or just don't add it) to hide it, <c>EnabledWhen(false)</c> to grey it out.
    /// </summary>
    protected abstract Task Build(MenuBuilder menu, T target);
}
