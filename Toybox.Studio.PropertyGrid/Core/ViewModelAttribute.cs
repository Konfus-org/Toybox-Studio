using System;

namespace Toybox.Studio.PropertyGrid;

/// <summary>
/// Routes a reflected member to a custom property editor by view-model <see cref="Type"/> (resolved through the
/// property-view registry), overriding the type-driven widget — the C# counterpart of the engine's
/// <c>[[tbx::view]]</c>. Naming the view-model type directly (e.g.
/// <c>[ViewModel(typeof(MaterialInstancePropertyViewModel))]</c>) keeps the routing type-safe instead of a magic
/// string id; the registry is keyed by the same type.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class ViewModelAttribute(Type viewModel) : Attribute
{
    public Type ViewModel { get; } = viewModel;
}
