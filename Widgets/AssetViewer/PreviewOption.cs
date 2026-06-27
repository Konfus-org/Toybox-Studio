using Toybox.Studio.Services.Project;

namespace Toybox.Studio.Widgets.AssetViewer;

/// <summary>
/// One choice in the asset viewer's preview picker: a human <see cref="Label"/> plus what it selects. For a
/// built-in mesh (material/texture preview), a surface material (model), or a background sky it carries the
/// resolved asset <see cref="Handle"/> (<see cref="AssetHandle.None"/> = the "Original"/"None" no-op
/// choice); for a sky material's projection it carries the <see cref="Token"/> ("skysphere"/"skybox").
/// </summary>
public sealed record PreviewOption(string Label, AssetHandle Handle = default, string Token = "");
