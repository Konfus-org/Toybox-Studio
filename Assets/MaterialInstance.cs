using System.Numerics;
using Avalonia.Media;
using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Assets;

/// <summary>
/// A <c>.mti</c> asset, mirrored from the engine's <c>MaterialInstance</c>: parameter/texture overrides
/// layered onto a base <see cref="Material"/>, Unreal-style. Render config stays on the base and is
/// shared by every instance. The typed setters and <c>Get*ParameterOr</c> accessors mirror the C++
/// surface; each edit reassigns <see cref="Overrides"/>, which is what pushes it. Also used by value
/// inside components (a <c>Sky</c>'s material, a post-processing effect) via
/// <see cref="MaterialInstanceConverter"/>.
/// </summary>
[Creatable("mti")]
public sealed partial class MaterialInstance : Asset
{
    /// <summary>The load of the existing instance with the given id (see <see cref="Asset.Loaded"/>).</summary>
    public MaterialInstance(ulong id = 0) : base(id) => Overrides = new MaterialOverrides();

    /// <summary>A fresh instance, authored at the given location by its first save.</summary>
    public MaterialInstance(string name, string directory = "")
        : base(name, directory)
        => Overrides = new MaterialOverrides();

    public MaterialInstance(Handle material)
        : this()
        => Material = material;

    public MaterialInstance(Handle material, MaterialOverrides overrides)
    {
        Material = material;
        Overrides = overrides;
    }

    /// <summary>The base <see cref="Assets.Material"/> this instance derives from (named "Base" in the
    /// editor); overrides layer on top of it.</summary>
    [EngineSync]
    public partial Handle Material { get; set; }

    [EngineSync(Converter = typeof(MaterialOverridesConverter))]
    public partial MaterialOverrides Overrides { get; set; }

    /// <summary>Overrides one parameter with a raw wire token (the typed setters below cover the
    /// common shapes).</summary>
    public void SetParameter(string name, JToken value) =>
        Overrides = Overrides.SetParameter(new MaterialParameter { Name = name, Data = value });

    public void SetTexture(string name, Handle texture) =>
        Overrides = Overrides.SetTexture(name, texture);

    public void SetBool(string name, bool value) => SetParameter(name, WireValue.Write(value));

    public void SetInt(string name, int value) => SetParameter(name, WireValue.Write(value));

    public void SetFloat(string name, float value) => SetParameter(name, WireValue.Write(value));

    public void SetDouble(string name, double value) => SetParameter(name, WireValue.Write(value));

    public void SetVector2(string name, Vector2 value) => SetParameter(name, WireValue.Write(value));

    public void SetVector3(string name, Vector3 value) => SetParameter(name, WireValue.Write(value));

    public void SetVector4(string name, Vector4 value) => SetParameter(name, WireValue.Write(value));

    public void SetColor(string name, Color value) => SetParameter(name, WireValue.Write(value));

    public bool GetBoolParameterOr(string name, bool fallback) =>
        WireValue.ReadBool(Overrides.GetParameter(name)?.Data, fallback);

    public int GetIntParameterOr(string name, int fallback) =>
        WireValue.ReadInt(Overrides.GetParameter(name)?.Data, fallback);

    public float GetFloatParameterOr(string name, float fallback) =>
        WireValue.ReadSingle(Overrides.GetParameter(name)?.Data, fallback);

    public double GetDoubleParameterOr(string name, double fallback) =>
        WireValue.ReadDouble(Overrides.GetParameter(name)?.Data, fallback);

    public Handle GetTextureHandleOr(string name, Handle fallback = default) =>
        Overrides.GetTexture(name)?.Texture ?? fallback;
}
