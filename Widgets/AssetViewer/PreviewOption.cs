namespace Toybox.Studio.Widgets.AssetViewer;

/// <summary>
/// One choice in the asset viewer's preview picker: a human <see cref="Label"/> and the engine
/// <see cref="Token"/> it sends (a built-in mesh for materials/textures, or a built-in material for
/// models).
/// </summary>
public sealed record PreviewOption(string Label, string Token);
