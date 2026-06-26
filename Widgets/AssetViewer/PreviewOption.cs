namespace Toybox.Studio.Widgets.AssetViewer;

/// <summary>
/// One choice in the asset viewer's preview picker, built from the engine's builtin.* catalog: a human
/// <see cref="Label"/> plus what it selects. For a built-in mesh (material/texture preview) or a sky
/// mode it carries the engine <see cref="Token"/>; for a built-in surface/sky material it carries the
/// asset <see cref="Id"/> (0 = the "Original"/"None" no-op choice).
/// </summary>
public sealed record PreviewOption(string Label, string Token = "", long Id = 0);
