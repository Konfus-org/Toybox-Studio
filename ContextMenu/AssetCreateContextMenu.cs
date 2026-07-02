using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Toybox.Studio.Dialogs;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Favorites;
using Toybox.Studio.Project;
using Toybox.Studio.Project.Assets;
using Toybox.Studio.Utils;
using Toybox.Studio.AssetBrowser;

namespace Toybox.Studio.ContextMenu;

/// <summary>
/// The Asset Browser's empty-space menu: a "New …" row per creatable asset kind (World, Material, Material
/// Instance, Shader, Script), plus Paste when the clipboard holds an asset. Routed for the
/// <see cref="AssetBrowserViewModel"/>. The menu OWNS the per-kind create prompts (a material's render type, a
/// shader's stage, a script's class name); it builds any seed and calls the <see cref="AssetFactory"/>, which OWNS
/// the authoring — no asset type creates itself.
/// </summary>
public sealed class AssetCreateContextMenu : ContextMenu<AssetBrowserViewModel>
{
    private readonly AssetFactory _factory;

    public AssetCreateContextMenu(FavoritesManager favorites, AssetFactory factory) : base(favorites) =>
        _factory = factory;

    protected override async Task Build(MenuBuilder menu, AssetBrowserViewModel target)
    {
        menu.Item("New World", Icon.Globe)
            .Keywords("create new add world").Color(Utils.Colors.Cyan)
            .Run(() => _factory.CreateWorldAsync());
        menu.Item("New Material", Icon.Palette)
            .Keywords("create new add material").Color(Utils.Colors.Yellow)
            .Run(CreateMaterialAsync);
        menu.Item("New Material Instance", Icon.Palette)
            .Keywords("create new add material instance").Color(Utils.Colors.Magenta)
            .Run(CreateMaterialInstanceAsync);
        menu.Item("New Shader", Icon.Sparkles)
            .Keywords("create new add shader").Color(Utils.Colors.Magenta)
            .Run(CreateShaderAsync);
        menu.Item("New Script", Icon.Code)
            .Keywords("create new add script").Color(Utils.Colors.Green)
            .Run(CreateScriptAsync);

        menu.Separator();
        menu.Item("Paste", Icon.ClipboardPaste).Gesture("Ctrl+V")
            .VisibleWhen(await _factory.CanPasteAsync().ContinueOnAnyContext())
            .Run(() => _factory.PasteAsync());
    }

    // Builds the create chooser for an asset-type enum from each member's [AssetType] presentation — the enum is
    // the single source of truth, so the rows can't drift out of step with it. The Key is the member name,
    // recovered to the enum value by the caller (the material seed / the shader stage's extension).
    private static IReadOnlyList<CatalogItem> Choices<TEnum>() where TEnum : struct, Enum =>
        AssetTypeAttribute.Options<TEnum>()
            .Select(option => new CatalogItem(
                option.Value.ToString(), option.Info.Label, option.Info.Description,
                option.Info.Icon, option.Info.Color.ToColor()))
            .ToList();

    // The "New Material" prompt: pick a render type, seed it as a typed-object node, hand off to the factory.
    private async Task CreateMaterialAsync()
    {
        var pick = await CatalogPicker.ShowAsync("New Material", "Choose a material type.", Choices<MaterialType>())
            .ContinueOnAnyContext();
        if (pick is null)
            return;

        var materialType = (int)Enum.Parse<MaterialType>(pick.Key);
        var seed = new JObject
        {
            ["type"] = new JObject { [EngineKeys.Type] = EngineTypes.Object, [EngineKeys.Value] = materialType },
        };
        await _factory.CreateJsonAsync("Material", "Assets/Materials", "Material", ".mat", seed)
            .ContinueOnAnyContext();
    }

    // The "New Material Instance" prompt: an instance derives from a base material, so pick one and seed it.
    private async Task CreateMaterialInstanceAsync()
    {
        var materials = _factory.Services.Catalog.AssetsOfType(["mat"]);
        if (materials.Count == 0)
        {
            await Popups.ShowErrorAsync(
                    "Can't create material instance", "Create a material first — an instance derives from one.")
                .ContinueOnAnyContext();
            return;
        }

        var pick = await AssetPicker.ShowAsync("Choose base material", materials, currentId: 0)
            .ContinueOnAnyContext();
        if (!pick.Confirmed)
            return;

        var seed = pick.Id != 0
            ? new JObject { ["material"] = new JObject { [EngineKeys.Type] = EngineTypes.Handle, [EngineKeys.Value] = pick.Id } }
            : null;
        await _factory.CreateJsonAsync("MaterialInstance", "Assets/Materials", "Material Instance", ".mti", seed)
            .ContinueOnAnyContext();
    }

    // The "New Shader" prompt: pick the pipeline stage, scaffold the source for its file extension.
    private async Task CreateShaderAsync()
    {
        var pick = await CatalogPicker.ShowAsync("New Shader", "Choose a shader stage.", Choices<ShaderType>())
            .ContinueOnAnyContext();
        if (pick is null)
            return;

        var stage = AssetTypeAttribute.Of(Enum.Parse<ShaderType>(pick.Key)).Extension;
        await _factory.ScaffoldShaderAsync(stage).ContinueOnAnyContext();
    }

    // The "New Script" prompt: ask for a class name, hand off to the factory's scaffold.
    private async Task CreateScriptAsync()
    {
        var entered = await Popups
            .PromptForTextAsync("New Script", "Script class name (e.g. MyBehaviour)", "NewScript", confirmText: "Create")
            .ContinueOnAnyContext();
        if (entered is null)
            return;

        await _factory.ScaffoldScriptAsync(entered).ContinueOnAnyContext();
    }
}
