using Toybox.Studio.EngineApi.Types;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.EngineApi.Types.Assets;

/// <summary>
/// An <c>.inputmap</c> asset, mirrored from the engine's <c>InputMap</c>: input schemes as data, so
/// keybindings live in a file instead of code. Games reference maps from <c>AppSettings.input_maps</c>
/// and the engine feeds their schemes to its <c>InputManager</c> at startup; the editor's own
/// keybindings are the same format, owned as a local file by the Keybindings service. The nested
/// values are records — edit by assigning a changed copy back to <see cref="Schemes"/>, which is what
/// pushes it.
/// </summary>
[Creatable("inputmap")]
public sealed partial class InputMap : Asset
{
    /// <summary>The load of the existing input map with the given id (see <see cref="Asset.Loaded"/>).</summary>
    public InputMap(ulong id = 0) : base(id) => Schemes = [];

    /// <summary>A fresh input map, authored at the given location by its first save.</summary>
    public InputMap(string name, string directory = "") : base(name, directory) => Schemes = [];

    [EngineSync(Converter = typeof(InputSchemeListConverter))]
    public partial IReadOnlyList<InputScheme> Schemes { get; set; }

    public InputScheme? GetScheme(string name) =>
        Schemes.FirstOrDefault(scheme => scheme.Name == name);
}
