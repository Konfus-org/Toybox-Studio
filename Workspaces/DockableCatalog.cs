using System.Reflection;
using Avalonia.Controls;
using Toybox.Studio.Logging;
using Toybox.Studio.Utils.Attributes;

namespace Toybox.Studio.Workspaces;

/// <summary>
/// Discovers every <c>[Dockable]</c> View in the given feature assemblies and exposes them as
/// <see cref="DockableDescriptor"/>s, with each view-model created through the factory the composition
/// root authored in <see cref="DockableFactories"/> — view-models are never service-registered, so a
/// scanned dockable without a factory fails here, at startup, with a message naming the fix. Parent
/// references are resolved and validated the same way: an unknown parent or a parent cycle logs a
/// warning and the dockable falls back to docking against the main window.
/// </summary>
public sealed class DockableCatalog
{
    public DockableCatalog(DockableFactories factories, Logger log, params Assembly[] assemblies)
    {
        var entries = Scan(assemblies).ToList();
        Dockables = entries
            .Select(entry => Build(entry, factories, log))
            .OrderBy(descriptor => descriptor.Slot)
            .ThenBy(descriptor => descriptor.Order)
            .ThenBy(descriptor => descriptor.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        ResolveParents(entries, log);
    }

    /// <summary>All registered dockables, in a deterministic order (by slot, then declared order, then title).</summary>
    public IReadOnlyList<DockableDescriptor> Dockables { get; }

    /// <summary>Finds every View type tagged with <see cref="DockableAttribute"/> and resolves its view-model type.</summary>
    private static IEnumerable<(Type View, DockableAttribute Attribute, Type ViewModel)> Scan(
        IReadOnlyList<Assembly> assemblies)
    {
        foreach (var assembly in assemblies.Distinct())
        {
            foreach (var type in assembly.GetTypes())
            {
                var attribute = type.GetCustomAttribute<DockableAttribute>(inherit: false);
                if (attribute is null)
                    continue;

                yield return (type, attribute, ResolveViewModelType(type, attribute));
            }
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

    private static DockableDescriptor Build(
        (Type View, DockableAttribute Attribute, Type ViewModel) entry, DockableFactories factories, Logger log)
    {
        var (viewType, attribute, viewModelType) = entry;
        return new DockableDescriptor
        {
            // The view-model type is the dockable's identity; its (namespace-independent) name is the stable
            // Dock-persistence key, so moving a panel between folders never invalidates saved layouts.
            Key = viewModelType.Name,
            Title = string.IsNullOrEmpty(attribute.Title) ? viewModelType.Name : attribute.Title,
            Icon = ParseIcon(attribute.Icon, viewType, log),
            Slot = attribute.Slot,
            Proportion = attribute.Proportion,
            Order = attribute.Order,
            FloatBounds = (attribute.FloatX, attribute.FloatY, attribute.FloatWidth, attribute.FloatHeight),
            Singleton = attribute.Singleton,
            ShowInWindowMenu = attribute.ShowInWindowMenu,
            CreateView = viewModel =>
            {
                var view = (Control)Activator.CreateInstance(viewType)!;
                view.DataContext = viewModel;
                return view;
            },
            CreateViewModel = factories.For(viewModelType),
            ViewModelType = viewModelType,
        };
    }

    // Wires each descriptor's Parent from its attribute's view-model-type reference, dropping (with a
    // warning) the ones that can't anchor a default layout: an unknown parent type, a Float parent
    // (it has no dock to split), or a parent chain that loops back on itself.
    private void ResolveParents(
        IEnumerable<(Type View, DockableAttribute Attribute, Type ViewModel)> entries, Logger log)
    {
        var byType = Dockables.ToDictionary(descriptor => descriptor.ViewModelType);
        foreach (var (viewType, attribute, viewModelType) in entries)
        {
            if (attribute.Parent is not { } parentType)
                continue;

            var descriptor = byType[viewModelType];
            if (!byType.TryGetValue(parentType, out var parent))
            {
                log.Warning($"[Dockable] on {viewType.Name}: parent {parentType.Name} is not a "
                            + "registered dockable; docking against the main window instead.");
                continue;
            }

            if (parent.Slot == DockSlot.Float)
            {
                log.Warning($"[Dockable] on {viewType.Name}: parent {parentType.Name} floats, so it has "
                            + "no default dock to split; docking against the main window instead.");
                continue;
            }

            descriptor.Parent = parent;
        }

        foreach (var descriptor in Dockables.Where(HasParentCycle))
        {
            log.Warning($"[Dockable] {descriptor.Key}: its parent chain loops back on itself; "
                        + "docking against the main window instead.");
            descriptor.Parent = null;
        }
    }

    // True when walking up from this descriptor never reaches the main window — i.e. its chain enters a
    // loop (whether or not the loop passes through the descriptor itself).
    private static bool HasParentCycle(DockableDescriptor descriptor)
    {
        var seen = new HashSet<DockableDescriptor> { descriptor };
        for (var ancestor = descriptor.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (!seen.Add(ancestor))
                return true;
        }

        return false;
    }

    // The attribute carries the icon as the kind's member name (a C# attribute can't carry the enum
    // alias across projects cleanly); an unknown name logs once and shows no glyph.
    private static Icon ParseIcon(string name, Type viewType, Logger log)
    {
        if (string.IsNullOrEmpty(name))
            return Icon.None;

        if (Enum.TryParse<Icon>(name, out var icon))
            return icon;

        log.Warning($"[Dockable] on {viewType.Name}: unknown icon '{name}'; showing none.");
        return Icon.None;
    }
}
