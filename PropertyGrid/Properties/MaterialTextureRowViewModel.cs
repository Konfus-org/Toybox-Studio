using System.Collections.Generic;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Project;

namespace Toybox.Studio.PropertyGrid;

/// <summary>
/// One texture DEFINITION row on a base <c>Material</c>: its sampler binding name and its default texture.
/// Editing either raises the owning section's commit so the whole texture list is re-serialised and pushed to the
/// buffered asset. The default texture reuses the standard asset picker (<see cref="HandlePickerPropertyViewModel"/>),
/// filtered to image assets.
/// </summary>
public sealed partial class MaterialTextureRowViewModel : ObservableObject
{
    private static readonly IReadOnlyList<string> ImageTypes = ["png", "jpg", "jpeg", "tga", "bmp"];

    private readonly Action _changed;

    // Seeded directly (bypassing the change hook) at construction, then edited live by the view.
    [ObservableProperty]
    private string _name;

    public MaterialTextureRowViewModel(
        string name, ulong textureId, int depth, Action changed, Action<MaterialTextureRowViewModel> remove)
    {
        _changed = changed;
        _name = name;

        var descriptor = new PropertyDescriptor
        {
            Name = name,
            Type = EngineTypes.Handle,
            Choices = ImageTypes,
        };
        var accessor = new JsonAccessor(new JValue(textureId), EngineTypes.Handle, changed);
        Picker = new HandlePickerPropertyViewModel(descriptor, accessor, AssetGridServices.Assets)
        {
            Depth = depth + 1,
        };
        RemoveCommand = new RelayCommand(() => remove(this));
    }

    /// <summary>The default texture picker; its live id is read back on serialise.</summary>
    public HandlePickerPropertyViewModel Picker { get; }

    /// <summary>Deletes this texture slot from its material.</summary>
    public ICommand RemoveCommand { get; }

    /// <summary>This row as one lean <c>{ name, texture }</c> element for the material's texture list.</summary>
    public JObject ToElement() => new()
    {
        ["name"] = new JValue(Name),
        ["texture"] = new JValue(Picker.CurrentId),
    };

    partial void OnNameChanged(string value) => _changed();
}
