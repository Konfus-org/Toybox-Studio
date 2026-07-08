using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Dock.Model.Avalonia.Controls;
using Toybox.Studio.AssetOwners;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Workspaces;

/// <summary>
/// The live owner-to-tab bindings, keyed by tool id: each ties an <see cref="AssetOwnerViewModel"/>'s
/// Title to its dock tab (so the '*' tracks dirty state) and its CloseRequested (the Cancel button) to
/// closing the tab. Bindings are held so re-templating / layout restore doesn't double-subscribe and
/// closing a tool can unsubscribe — a stale handler would keep an abandoned Tool alive. Bookkeeping
/// only: the <see cref="Workspace"/> decides when to bind and unbind, and supplies what closing
/// the tab means.
/// </summary>
public sealed class OwnerTabBinder
{
    private readonly Dictionary<string, Binding> _bindings = [];

    /// <summary>
    /// Mirrors an <see cref="AssetOwnerViewModel"/>'s Title onto its dock tab (now and on every change)
    /// and wires its Cancel button to <paramref name="closeTab"/>. No-op for a non-owner view-model.
    /// Idempotent across the repeated attach passes a layout restore / re-templating triggers:
    /// re-binding the same owner just re-derives the title (a serialized Title may carry a stale '*');
    /// a different owner replaces the old subscription.
    /// </summary>
    public void Bind(Tool tool, object? viewModel, Action closeTab)
    {
        if (viewModel is not AssetOwnerViewModel owner)
            return;

        if (_bindings.TryGetValue(tool.Id, out var existing))
        {
            if (ReferenceEquals(existing.Owner, owner))
            {
                tool.Title = owner.Title;
                return;
            }

            existing.Owner.PropertyChanged -= existing.TitleHandler;
            existing.Owner.CloseRequested -= existing.CloseHandler;
        }

        void TitleHandler(object? _, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(AssetOwnerViewModel.Title))
                Dispatch.To(DispatchContext.UI, () => tool.Title = owner.Title);
        }

        owner.PropertyChanged += TitleHandler;
        owner.CloseRequested += closeTab;
        _bindings[tool.Id] = new Binding(owner, TitleHandler, closeTab);
        tool.Title = owner.Title;
    }

    /// <summary>The owner bound to a tool id, when the tool hosts one.</summary>
    public bool TryGetOwner(string toolId, [NotNullWhen(true)] out AssetOwnerViewModel? owner)
    {
        if (_bindings.TryGetValue(toolId, out var binding))
        {
            owner = binding.Owner;
            return true;
        }

        owner = null;
        return false;
    }

    /// <summary>Releases a tool's binding; no-op when none is held.</summary>
    public void Unbind(string toolId)
    {
        if (_bindings.Remove(toolId, out var binding))
        {
            binding.Owner.PropertyChanged -= binding.TitleHandler;
            binding.Owner.CloseRequested -= binding.CloseHandler;
        }
    }

    /// <summary>Releases every binding — run before a layout rebuild abandons the old tools, so the
    /// fresh tools subscribe cleanly instead of dead tabs staying alive on old handlers.</summary>
    public void UnbindAll()
    {
        foreach (var toolId in _bindings.Keys.ToList())
            Unbind(toolId);
    }

    private sealed record Binding(
        AssetOwnerViewModel Owner, PropertyChangedEventHandler TitleHandler, Action CloseHandler);
}
