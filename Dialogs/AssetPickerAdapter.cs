using System.Collections.Generic;
using System.Threading.Tasks;
using Toybox.Studio.Project;
using Toybox.Studio.PropertyGrid;

namespace Toybox.Studio.Dialogs;

/// <summary>
/// Adapts the dialog-layer <see cref="AssetPicker"/> to the property grid's <see cref="IAssetPicker"/>, so the
/// grid's picker editors open the modal chooser without depending on the dialog layer. Wired to
/// <see cref="PropertyViewRegistry.AssetPicker"/> at startup.
/// </summary>
public sealed class AssetPickerAdapter : IAssetPicker
{
    public Task<AssetPick> ShowAsync(string title, IReadOnlyList<AssetMeta> options, ulong currentId) =>
        AssetPicker.ShowAsync(title, options, currentId);
}
