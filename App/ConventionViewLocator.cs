using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace Toybox.Studio;

/// <summary>
/// Resolves a view for a view-model by the studio's <c>XxxViewModel → XxxView</c> convention, across
/// assemblies, at runtime — so a <c>ContentControl</c> bound to a view-model that isn't templated
/// explicitly (the viewport's node overlay slot) renders its matching view without the hosting project
/// having to reference the feature. Registered app-wide in <c>App.axaml</c>. Only matches when a co-named
/// view type actually exists in the view-model's assembly, so it never hijacks content it can't resolve.
/// </summary>
public sealed class ConventionViewLocator : IDataTemplate
{
    private static readonly Dictionary<Type, Type?> Cache = [];

    public bool Match(object? data) => data is not null && Resolve(data.GetType()) is not null;

    public Control? Build(object? data)
    {
        if (data is null || Resolve(data.GetType()) is not { } viewType)
            return null;
        return Activator.CreateInstance(viewType) as Control;
    }

    private static Type? Resolve(Type viewModelType)
    {
        if (Cache.TryGetValue(viewModelType, out var cached))
            return cached;

        Type? viewType = null;
        if (viewModelType.FullName is { } name && name.EndsWith("ViewModel", StringComparison.Ordinal))
            viewType = viewModelType.Assembly.GetType(name[..^"Model".Length]); // XxxViewModel → XxxView

        return Cache[viewModelType] = viewType;
    }
}
