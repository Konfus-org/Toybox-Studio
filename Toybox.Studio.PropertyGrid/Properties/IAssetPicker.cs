using System.Collections.Generic;
using System.Threading.Tasks;
using Toybox.Studio.Project;

namespace Toybox.Studio.PropertyGrid;

/// <summary>
/// Shows the modal asset/entity chooser, inverting the property grid's dependency on the dialog layer: the
/// dialog layer supplies the implementation and the grid's picker editors call it through
/// <see cref="PropertyViewRegistry.AssetPicker"/>.
/// </summary>
public interface IAssetPicker
{
    /// <summary>Opens the chooser titled <paramref name="title"/> over <paramref name="options"/>, with
    /// <paramref name="currentId"/> pre-selected; returns the user's pick.</summary>
    Task<AssetPick> ShowAsync(string title, IReadOnlyList<AssetMeta> options, ulong currentId);
}
