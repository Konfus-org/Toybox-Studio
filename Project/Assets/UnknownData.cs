namespace Toybox.Studio.Project.Assets;

/// <summary>
/// The fallback payload for a catalog entry whose file type has no dedicated data type — so the generic lifecycle
/// ops (rename / delete / duplicate / reveal) still work on arbitrary project files through an
/// <c>Asset&lt;UnknownData&gt;</c>. It models no fields; the inspector shows nothing editable.
/// </summary>
public sealed class UnknownData : AssetData
{
}
