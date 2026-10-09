using System.Numerics;
using WildRenderingSharp.Graphics;
using WildRenderingSharp.Profiles.Totk.Atmosphere;

namespace WildRenderingSharp.Profiles.Totk.Sky;

/// <summary>
/// The uniform blocks of the cloud dome's <c>agl_cloud</c> program: Common, which is the capture of the game's own block with every
/// identified slot overwritten from live data, the dome's View, and the renderer's own distance fade.
/// </summary>
static class CloudBlocks
{
    public static readonly UboSpec Common = new("Common", 20, 768);
    public static readonly UboSpec View = new("View", 21, 256);
    public static readonly UboSpec Fade = new("Fade", CloudDistanceFade.Binding, 48);

    static readonly UboMatrix ModelView = new(4, 4);
    static readonly UboMatrix Projection = new(8, 4);
    const int ZOffset = 12;

    public static Ubo BuildCommon(EnvPalette palette, EnvPalette.CloudLayer cloud, CloudPostFxShared shared, CloudPostFxLayer layer,
        Vector3 sunWorld, float seconds, float skyHeightAboveCamera, float skyColorGain)
    {
        var block = new UboWriter(Common, CloudUboBaseline.Common());
        WriteScrolling(block, layer, seconds);
        WriteShading(block, layer, seconds);
        WriteDistanceTerms(block, layer);
        WriteColours(block, palette, cloud, shared, skyColorGain);
        WriteDome(block, layer, skyHeightAboveCamera, sunWorld);
        return block.ToUbo("cloud_common");
    }

    public static Ubo BuildView(Matrix4x4 view, Matrix4x4 proj, Vector3 cameraEye, CloudPostFxLayer layer, float skyHeightAboveCamera, float domeScale)
    {
        // The unit dome scaled to the sky extent, with its height scaled separately, centred on the camera.
        float radius = layer.SkyScale * domeScale;
        float height = skyHeightAboveCamera * domeScale;
        var model = Matrix4x4.CreateScale(radius, height, radius) * Matrix4x4.CreateTranslation(cameraEye);

        var block = new UboWriter(View);
        block.Set(ModelView, model * view);
        block.Set(Projection, proj);
        block.Set(ZOffset, 0, CloudUboBaseline.ZOffsetParam);
        return block.ToUbo("cloud_view");
    }

    public static Ubo BuildFade(float startDistance, float ramp, bool exponential, float strength, Vector3 extents, Vector3 skyColor)
    {
        var block = new UboWriter(Fade);
        block.Set(0, MathF.Max(0f, startDistance), MathF.Max(0f, ramp), exponential ? 1f : 0f, strength);
        block.SetXyz(1, extents);
        block.SetXyz(2, skyColor);
        return block.ToUbo("cloud_fade");
    }

    // The slots of each layer field are the order the game's own fill routine writes them in. The noise offsets are speed times time,
    // spread over slots 0.w to 1.z.
    static void WriteScrolling(UboWriter block, CloudPostFxLayer layer, float seconds)
    {
        block.Set(0, 0, seconds);
        block.Set(0, 1, layer.Distotion);
        block.Set(0, 2, layer.Density);
        block.Set(0, 3, layer.NoiseSpeed1X * seconds);
        block.Set(1, 0, layer.NoiseSpeed1Y * seconds);
        block.Set(1, 1, layer.NoiseSpeed2X * seconds);
        block.Set(1, 2, layer.NoiseSpeed2Y * seconds);
        block.Set(1, 3, layer.NoiseScale1);
        block.Set(2, 0, layer.NoiseScale2);
        block.Set(2, 1, layer.NoiseDensity1);
        block.Set(2, 2, layer.NoiseDensity2);
        block.Set(2, 3, layer.EmbossWidth);
    }

    static void WriteShading(UboWriter block, CloudPostFxLayer layer, float seconds)
    {
        block.Set(3, layer.EmbossDensity, layer.HilightPower, layer.ShadowPower, layer.HighlightRange);

        // The alpha multiplier is pre-divided by what the threshold removes.
        float alphaMul = layer.AlphaThreshold != 1f ? layer.AlphaMul / (1f - layer.AlphaThreshold) : layer.AlphaMul;
        block.Set(4, layer.HighlightAmbient, alphaMul, layer.AlphaThreshold, layer.BacklightPower);
        block.Set(5, layer.BacklightRange, layer.BacklightParam0, layer.BacklightParam1, layer.BaseTexScale);

        // The base texture's offsets, the same for both texture pairs.
        float scrollX = layer.BaseTexScrollSpdX * seconds, scrollY = layer.BaseTexScrollSpdY * seconds;
        block.Set(6, scrollX, scrollY, scrollX, scrollY);
    }

    static void WriteDistanceTerms(UboWriter block, CloudPostFxLayer layer)
    {
        block.Set(7, layer.FarUVPow, layer.FarUVMul, layer.FarDensityChgStart, layer.FarDensityChgEnd);
        block.Set(8, layer.FarDensityChgPower, layer.FarAlphaChgStart, layer.FarAlphaChgEnd, layer.FarAlphaChgPower);
        block.Set(27, 0, layer.FarDistotionChgPower);
        block.Set(25, 3, layer.ScatterHeight);
        block.Set(26, 1, layer.SunOccChkSize);
        block.Set(26, 2, layer.FarDistotionChgStart);
        block.Set(26, 3, layer.FarDistotionChgEnd);

        // Zeroed so the distance fade decides alpha; a non-zero value here is a constant floor under it.
        block.Set(45, 0, 0f);
    }

    static void WriteColours(UboWriter block, EnvPalette palette, EnvPalette.CloudLayer cloud, CloudPostFxShared shared, float skyColorGain)
    {
        // The last factor the shader applies is the gain; the unscaled reciprocal stays so the fog term scales the same way.
        block.Set(25, 1, shared.CloudColorScale * skyColorGain);
        block.Set(25, 2, shared.CloudColorScale > 1e-6f ? 1f / shared.CloudColorScale : 0f);

        // Colour times intensity in .xyz and the raw intensity in .w, which the shader reads structurally. Only the radiance terms carry the gain.
        block.SetXyz(28, cloud.ColorBase * cloud.IntensityBase);
        block.Set(28, 3, cloud.IntensityBase);
        block.SetXyz(29, cloud.ColorHilight * cloud.IntensityHilight);
        block.Set(29, 3, cloud.IntensityHilight);
        block.SetXyz(30, cloud.ColorShadow * cloud.IntensityShadow);
        block.Set(30, 3, cloud.IntensityShadow);
        block.SetXyz(31, cloud.ColorBackLight);
        block.Set(31, 3, 0f);

        block.SetXyz(36, palette.FogColor);
        block.Set(37, 0, palette.FogEnd > 1e-6f ? 1f / palette.FogEnd : 0f);
        block.Set(41, 0, palette.ScatterFogAttenuation);
        block.Set(41, 1, palette.ScatterFogHorizontal);
    }

    // The dome's direction matrix is a diagonal of the dome's scale and its height above the camera, and the sun slot holds the direction light travels.
    static void WriteDome(UboWriter block, CloudPostFxLayer layer, float skyHeightAboveCamera, Vector3 sunWorld)
    {
        block.SetXyz(32, new Vector3(layer.SkyScale, 0f, 0f));
        block.SetXyz(33, new Vector3(0f, skyHeightAboveCamera, 0f));
        block.SetXyz(34, new Vector3(0f, 0f, layer.SkyScale));
        block.SetXyz(42, -sunWorld);
    }
}
