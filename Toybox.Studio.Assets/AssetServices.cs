using Toybox.Assets;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Clipboards;

namespace Toybox.Studio.Project;

/// <summary>
/// The editor services every <see cref="Asset"/> operation routes through, bundled into one injected value so an
/// asset handle (a cheap object minted all over the UI) carries a single reference instead of eight loose fields.
/// Built once as a DI singleton; the <see cref="AssetFactory"/> hands it to each handle through its constructor.
/// </summary>
public sealed class AssetServices(
    Engine engine,
    AssetCatalog catalog,
    AssetSelection selection,
    ProjectManager projects,
    ProjectBuilder builder,
    IAssetOpener opener,
    IUserPrompt prompt,
    Clipboards.Clipboard clipboard)
{
    public Engine Engine { get; } = engine;

    public AssetCatalog Catalog { get; } = catalog;

    public AssetSelection Selection { get; } = selection;

    public ProjectManager Projects { get; } = projects;

    public ProjectBuilder Builder { get; } = builder;

    public IAssetOpener Opener { get; } = opener;

    /// <summary>User prompts (error / confirm / rename) asset operations raise, via the dialog layer.</summary>
    public IUserPrompt Prompt { get; } = prompt;

    public Clipboards.Clipboard Clipboard { get; } = clipboard;
}
