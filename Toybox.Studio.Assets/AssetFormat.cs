namespace Toybox.Studio.Project;

/// <summary>
/// How an asset's on-disk payload is encoded — the "type description for the data" an <see cref="Asset{TData}"/>
/// reads (from its data type's <see cref="AssetInfoAttribute"/>) to load/save generically. <see cref="Json"/> is
/// the reflected describe body (the engine round-trip); <see cref="PlainText"/> is raw text (a script's source),
/// read/written as the file's text with no JSON in the asset layer.
/// </summary>
public enum AssetFormat
{
    Json,
    PlainText,
}
