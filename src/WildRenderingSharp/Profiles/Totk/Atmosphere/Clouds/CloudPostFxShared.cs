using System.Numerics;
using System.Text.Json;
using WildRenderingSharp.Profiles.Totk.Atmosphere.Palettes;

namespace WildRenderingSharp.Profiles.Totk.Atmosphere.Clouds;

/// <summary>
/// The real <c>agl::fx::Cloud</c> top-level "Cloud" object - settings shared by both layers (as opposed to <see
/// cref="CloudPostFxLayer"/>'s per-layer settings).
/// </summary>
public sealed class CloudPostFxShared
{
    public bool IsEnable = true;
    public bool IsDrawReduceBuffer = true;
    public bool IsDisableFarClip = true;
    public bool IsDisableDepthTest;
    public int DrawOrder;
    public float CloudColorScale = 1f;
    public Vector3 FogColor = new(0.4f, 0.6f, 0.9f);
    public float FogNear = -5f;
    public float FogFar = 300f;
    public float AttenuationForSky = 0.5f;
    public int SunOccBufSize = 8;

    public static readonly CloudPostFxShared Default = new();

    internal static CloudPostFxShared FromJson(JsonElement obj)
    {
        var d = Default;
        return new CloudPostFxShared
        {
            IsEnable = SkyPostFx.ReadBool(obj, "IsEnable", d.IsEnable),
            IsDrawReduceBuffer = SkyPostFx.ReadBool(obj, "mIsDrawReduceBuffer", d.IsDrawReduceBuffer),
            IsDisableFarClip = SkyPostFx.ReadBool(obj, "mIsDisableFarClip", d.IsDisableFarClip),
            IsDisableDepthTest = SkyPostFx.ReadBool(obj, "mIsDisableDepthTest", d.IsDisableDepthTest),
            DrawOrder = SkyPostFx.ReadInt(obj, "mDrawOrder", d.DrawOrder),
            CloudColorScale = SkyPostFx.ReadFloat(obj, "mCloudColorScale", d.CloudColorScale),
            FogColor = SkyPostFx.ReadColorRgb(obj, "mFogColor", d.FogColor),
            FogNear = SkyPostFx.ReadFloat(obj, "mFogNear", d.FogNear),
            FogFar = SkyPostFx.ReadFloat(obj, "mFogFar", d.FogFar),
            AttenuationForSky = SkyPostFx.ReadFloat(obj, "mAttenuationForSky", d.AttenuationForSky),
            SunOccBufSize = SkyPostFx.ReadInt(obj, "mSunOccBufSize", d.SunOccBufSize),
        };
    }
}
