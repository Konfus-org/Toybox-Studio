using Toybox.Assets;

namespace Toybox.Studio.Project.Assets;

/// <summary>
/// The strongly-typed payload of a C++ script asset (the data behind an <c>Asset&lt;Script&gt;</c>): its source
/// TEXT, held in <see cref="AssetData.Text"/>. A plain-text asset — the source (<c>.h</c>, behind the
/// self-describing <c>.h.meta</c> identity) is read/written directly by <see cref="Asset{TData}"/>, with no JSON
/// in the asset layer. The <c>.h</c>/<c>.cpp</c>/<c>.h.meta</c> trio travels together (Companions), so a duplicate
/// copies the whole set; a script change rebuilds (<see cref="AssetInfoAttribute.AffectsBuild"/>). All of that is
/// declared here as data + attributes — the lifecycle is the generic <see cref="Asset"/> behaviour, not a per-kind
/// subclass. Authoring (the source scaffold) lives in the <c>AssetFactory</c>.
/// </summary>
[AssetInfo("h", "hpp", "hh",
    Format = AssetFormat.PlainText,
    Companions = ["cpp", "cc", "cxx"],
    AffectsBuild = true)]
public sealed class Script : AssetData;
