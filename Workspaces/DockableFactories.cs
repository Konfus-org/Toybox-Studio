namespace Toybox.Studio.Workspaces;

/// <summary>
/// The composition root's explicit view-model factories for dockables, keyed by view-model type. A
/// dockable's view-model is never registered in the service container — panels aren't injectable, and
/// their lifetime belongs to the workspace, not the provider. Instead the Launcher authors one factory
/// per <c>[Dockable]</c> here (closing over whatever services the view-model's constructor takes), and
/// the workspace invokes it once per opened panel. A factory that returns a closure-held instance makes
/// the panel's state survive close/reopen; a plain <c>new</c> gives every open a fresh view-model.
/// </summary>
public sealed class DockableFactories
{
    private readonly Dictionary<Type, Func<object>> _factories = [];

    /// <summary>Registers the factory for <typeparamref name="TViewModel"/>'s dockable. Chainable.</summary>
    public DockableFactories Add<TViewModel>(Func<TViewModel> factory) where TViewModel : class
    {
        _factories[typeof(TViewModel)] = factory;
        return this;
    }

    /// <summary>
    /// The factory for a dockable's view-model type. Throws for an unregistered type — a scanned
    /// <c>[Dockable]</c> whose factory was never authored is a composition error, surfaced at startup
    /// (when the catalog builds its descriptors) rather than on first open.
    /// </summary>
    public Func<object> For(Type viewModelType)
    {
        if (_factories.TryGetValue(viewModelType, out var factory))
            return factory;

        throw new InvalidOperationException(
            $"No dockable view-model factory is registered for {viewModelType.Name}. "
            + "Add one to the DockableFactories composed in Launcher.ConfigureServices.");
    }
}
