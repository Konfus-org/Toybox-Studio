using Toybox.Studio.Services.EngineApi;
using Toybox.Studio.Services.Project;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// A handle/asset reference: the current asset shows as a link of its resolved name (clicking reveals it in
/// the OS file explorer), and the picker button opens a modal chooser of assets matching the node's
/// <c>$choices</c> type list, committing the chosen asset's id back to the backing token. Routed purely from
/// the "handle" type token — no [[editor::view]] tag needed. Shares its view and behaviour with
/// <see cref="EntityPickerPropertyViewModel"/> via <see cref="PickerPropertyViewModel"/>.
/// </summary>
public sealed class HandlePickerPropertyViewModel : PickerPropertyViewModel
{
    private readonly AssetCatalog? _catalog;
    private readonly IReadOnlyList<string>? _typeFilter;

    public HandlePickerPropertyViewModel(PropertyNode node, Action? commit, AssetCatalog? catalog)
        : base(node, commit)
    {
        _catalog = catalog;
        _typeFilter = node.Choices;
        if (catalog is not null)
            catalog.Changed += OnCatalogUpdated;
        RefreshDisplay();
    }

    public override string IconName => "Target";

    public override string PickTooltip => "Pick from asset database";

    // An asset is a file on disk, so a set reference reveals itself in the OS file explorer.
    protected override bool RevealsOnActivate => true;

    private void OnCatalogUpdated() => Dispatch.To(DispatchContext.UI, RefreshDisplay);

    protected override string ResolveDisplayName()
    {
        var id = CurrentId;
        if (id == 0)
            return "None";

        // The catalog keys ids as signed long; reinterpret the full 64 bits so a high-bit handle resolves.
        return _catalog?.ResolveName(unchecked((long)id)) ?? $"#{id}";
    }

    protected override (string Title, IReadOnlyList<AssetInfo> Options) BuildChoices()
    {
        var options = _catalog?.AssetsOfType(_typeFilter) ?? [];
        var title = _typeFilter is { Count: > 0 }
            ? $"Select {string.Join(" / ", _typeFilter)}"
            : "Select asset";
        return (title, options);
    }

    protected override void Reveal()
    {
        if (CurrentId != 0)
            _catalog?.Activate(unchecked((long)CurrentId));
    }
}
