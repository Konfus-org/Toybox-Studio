using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Toybox.Studio.Workspace;

/// <summary>
/// Discovers every <c>[Dockable]</c> View in the Studio assembly and exposes them as
/// <see cref="DockableDescriptor"/>s. Registration is two-phase: <see cref="RegisterDockables"/> runs at
/// composition time (registering each dockable's view-model in DI), and the instance — resolved from the
/// container — builds the descriptors with view factories bound to the live service provider.
/// </summary>
public sealed class DockableCatalog
{
    private readonly IServiceProvider _services;

    public DockableCatalog(IServiceProvider services)
    {
        _services = services;
        Dockables = Scan()
            .Select(Build)
            .OrderBy(d => d.Slot)
            .ThenBy(d => d.Order)
            .ThenBy(d => d.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>All registered dockables, in a deterministic order (by slot, then declared order, then title).</summary>
    public IReadOnlyList<DockableDescriptor> Dockables { get; }

    /// <summary>
    /// Scans for <c>[Dockable]</c> Views, registers each one's view-model as a singleton, and registers
    /// the catalog itself. Call from <c>ConfigureServices</c>. View-models are added with
    /// <c>TryAddSingleton</c> so a manually-registered or shared view-model isn't duplicated.
    /// </summary>
    public static void RegisterDockables(IServiceCollection services)
    {
        foreach (var (_, _, viewModelType) in Scan())
            services.TryAddSingleton(viewModelType);

        services.AddSingleton<DockableCatalog>();
    }

    /// <summary>Finds every View type tagged with <see cref="DockableAttribute"/> and resolves its view-model type.</summary>
    private static IEnumerable<(Type View, DockableAttribute Attribute, Type ViewModel)> Scan()
    {
        foreach (var type in typeof(DockableCatalog).Assembly.GetTypes())
        {
            var attribute = type.GetCustomAttribute<DockableAttribute>(inherit: false);
            if (attribute is null)
                continue;

            yield return (type, attribute, ResolveViewModelType(type, attribute));
        }
    }

    /// <summary>
    /// The explicit <see cref="DockableAttribute.ViewModel"/> if set, else the
    /// <c>XxxView → XxxViewModel</c> same-namespace, same-assembly convention.
    /// </summary>
    private static Type ResolveViewModelType(Type viewType, DockableAttribute attribute)
    {
        if (attribute.ViewModel is { } explicitType)
            return explicitType;

        var name = viewType.Name.EndsWith("View", StringComparison.Ordinal)
            ? viewType.Name[..^"View".Length] + "ViewModel"
            : viewType.Name + "ViewModel";

        var fullName = viewType.Namespace is { } ns ? $"{ns}.{name}" : name;
        return viewType.Assembly.GetType(fullName)
               ?? throw new InvalidOperationException(
                   $"[Dockable] on {viewType.Name}: could not find view-model '{fullName}'. "
                   + "Set ViewModel = typeof(...) on the attribute if it doesn't follow the "
                   + "XxxView → XxxViewModel convention.");
    }

    private DockableDescriptor Build((Type View, DockableAttribute Attribute, Type ViewModel) entry)
    {
        var (viewType, attribute, viewModelType) = entry;
        return new DockableDescriptor
        {
            Id = attribute.Id,
            Title = string.IsNullOrEmpty(attribute.Title) ? attribute.Id : attribute.Title,
            Icon = attribute.Icon,
            FloatSize = (attribute.Width, attribute.Height),
            Slot = attribute.Slot,
            Proportion = attribute.Proportion,
            Order = attribute.Order,
            CreateView = () =>
            {
                var view = (Control)Activator.CreateInstance(viewType)!;
                view.DataContext = _services.GetRequiredService(viewModelType);
                return view;
            },
        };
    }
}
