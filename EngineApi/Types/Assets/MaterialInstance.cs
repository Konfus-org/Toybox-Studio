using Avalonia.Media;
using System.Numerics;
using Toybox.Studio.EngineApi.Types;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Utils.Attributes;

namespace Toybox.Studio.EngineApi.Types.Assets;

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
    [AssetType("mat", "mti")]
    public partial Handle Material { get; set; }

    [EngineSync(Converter = typeof(MaterialOverridesConverter))]
    public partial MaterialOverrides Overrides { get; set; }

    /// <summary>Overrides one parameter with a typed value (the shape-specific setters below build the
    /// common ones).</summary>
    public void SetParameter(string name, MaterialValue value) =>
        Overrides = Overrides.SetParameter(new MaterialParameter { Name = name, Value = value });

    public void SetTexture(string name, Handle texture) =>
        Overrides = Overrides.SetTexture(name, texture);

    public void SetBool(string name, bool value) => SetParameter(name, MaterialValue.OfBool(value));

    public void SetInt(string name, int value) => SetParameter(name, MaterialValue.OfInt(value));

    public void SetFloat(string name, float value) => SetParameter(name, MaterialValue.OfFloat(value));

    // The engine variant carries a single-precision scalar, so a double collapses to float — the
    // parameter is edited and stored as a float.
    public void SetDouble(string name, double value) => SetParameter(name, MaterialValue.OfFloat((float)value));

    public void SetVector2(string name, Vector2 value) => SetParameter(name, MaterialValue.OfVector2(value));

    public void SetVector3(string name, Vector3 value) => SetParameter(name, MaterialValue.OfVector3(value));

    public void SetVector4(string name, Vector4 value) => SetParameter(name, MaterialValue.OfVector4(value));

    public void SetColor(string name, Color value) => SetParameter(name, MaterialValue.OfColor(value));

    public bool GetBoolParameterOr(string name, bool fallback) =>
        WireValue.ReadBool(Overrides.GetParameter(name)?.Value.ToWire(), fallback);

    public int GetIntParameterOr(string name, int fallback) =>
        WireValue.ReadInt(Overrides.GetParameter(name)?.Value.ToWire(), fallback);

    public float GetFloatParameterOr(string name, float fallback) =>
        WireValue.ReadSingle(Overrides.GetParameter(name)?.Value.ToWire(), fallback);

    public double GetDoubleParameterOr(string name, double fallback) =>
        WireValue.ReadDouble(Overrides.GetParameter(name)?.Value.ToWire(), fallback);

    public Handle GetTextureHandleOr(string name, Handle fallback = default) =>
        Overrides.GetTexture(name)?.Texture ?? fallback;
}
