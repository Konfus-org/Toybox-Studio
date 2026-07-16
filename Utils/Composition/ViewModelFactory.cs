using Microsoft.Extensions.DependencyInjection;

namespace Toybox.Studio.Utils.Composition;

/// <summary>
/// Builds view-models on demand: each constructor's service dependencies come from the container, and the
/// caller passes any runtime (non-service) arguments positionally — matched to the remaining parameters by
/// type. This is the one class in the app that holds the service provider; every other type still asks for
/// exactly the services it needs. View-models are never services — every call builds a fresh instance, and
/// any state a panel must keep across close/reopen lives in a service the fresh view-model reads back from.
/// </summary>
public sealed class ViewModelFactory
{
    private readonly IServiceProvider _provider;

    public ViewModelFactory(IServiceProvider provider) => _provider = provider;

    /// <summary>
    /// Creates a fresh <typeparamref name="TViewModel"/>, resolving its constructor's services from the
    /// container and matching <paramref name="runtimeArgs"/> to the remaining parameters by type. Pass a
    /// runtime argument for every parameter that isn't a registered service and has no usable default; never
    /// pass <c>null</c> positionally (a null has no type to match) — omit it and let the parameter default fill.
    /// </summary>
    public TViewModel Create<TViewModel>(params object[] runtimeArgs) where TViewModel : class =>
        (TViewModel)Create(typeof(TViewModel), runtimeArgs);

    /// <summary>Non-generic form, for callers that only hold the view-model <see cref="Type"/> (the docking catalog).</summary>
    public object Create(Type viewModelType, params object[] runtimeArgs) =>
        ActivatorUtilities.CreateInstance(_provider, viewModelType, runtimeArgs);
}
