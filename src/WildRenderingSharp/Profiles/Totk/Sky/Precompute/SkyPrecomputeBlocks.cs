using System.Numerics;
using WildRenderingSharp.Graphics.Ubos;
using WildRenderingSharp.Profiles.Totk.Atmosphere.Palettes;
using WildRenderingSharp.Profiles.Totk.Sky.PostFx;
using static WildRenderingSharp.Profiles.Totk.Sky.Precompute.SkyPrecomputePass;

namespace WildRenderingSharp.Profiles.Totk.Sky.Precompute;

/// <summary>The uniform blocks of the sky precompute chain: SizeInfo, Config for the solve passes, and RenderInfo for the layer index and the bake.</summary>
static class SkyPrecomputeBlocks
{
    public static readonly UboSpec SizeInfo = new("SizeInfo", 22, 144);
    public static readonly UboSpec Config = new("Config", 23, 48);
    public static readonly UboSpec RenderInfo = new("RenderInfo", 24, 128);

    // The RenderInfo the game binds to sky_bake_inscatter, as captured. The slots the bake knows are overwritten; the rest are kept
    // because zeroing them is demonstrably wrong.
    static readonly float[] BakeRenderInfoCapture =
    [
        0.00411f, 0.011275f, 0.028364f, 0.0018f,     // [0] betaR.rgb, betaM
        0.75f, 1f, 12f, 0f,                          // [1] unidentified
        0.0092173358f, 0.67337108f, 0.73924726f, 0f, // [2] sun direction, Y-up
        18f, 16.588242f, 15.3f, 0.0039553195f,       // [3] unidentified
        59.999023f, 879.13525f, 0.0009765625f, 3.4641016f, // [4] unidentified; .x is Rt-Rg
        0.95547581f, 25000f, 24999.045f, 0.040001526f,     // [5] unidentified; range-ish
        0.5f, 0.4f, 0.3f, 1f,                        // [6] GroundColor, .w=1
        0f, 0f, 0f, 0f,                              // [7] zero in the capture
    ];

    public static Ubo BuildSizeInfo()
    {
        var block = new UboWriter(SizeInfo);
        WriteDimensions(block, 0, TransmittanceW, TransmittanceH);
        WriteDimensions(block, 1, IrradianceW, IrradianceH);
        WriteDimensions(block, 2, BakedInscatterW, BakedInscatterH);
        WriteDimensions(block, 3, RangeTransmittanceW, RangeTransmittanceH);
        WriteAxis(block, 4, ResMu);
        WriteAxis(block, 5, ResMuS);
        WriteAxis(block, 6, ResNu);
        WriteAxis(block, 7, ResR);
        block.Set(8, 0, Rg);
        block.Set(8, 1, Rt);
        return block.ToUbo("sky_sizeinfo");
    }

    // The palette's amplifiers scale the coefficients, which is what changes the sky's colour per palette; Config[1].x is the Mie
    // phase asymmetry the solve passes use as g.
    public static Ubo BuildConfig(SkyPostFx postfx, int layer, float rayleighAmplifier = 1f, float mieAmplifier = 1f, float mieAsymmetry = 0.75f)
    {
        var block = new UboWriter(Config);
        var rayleigh = postfx.RayleighScatteringCoeff * rayleighAmplifier;
        block.Set(0, rayleigh.X, rayleigh.Y, rayleigh.Z, postfx.MieScatteringCoeff * mieAmplifier);
        block.Set(1, mieAsymmetry, postfx.RayleighBaseHeight, postfx.MieBaseHeight, 0f);

        var (dmin, dmax, dminp, dmaxp) = SliceGeometry(layer);
        block.Set(2, dmin, dmax, dminp, dmaxp);
        return block.ToUbo("sky_config");
    }

    // The RenderInfo of a solve pass, which reads only the depth of the layer it is drawing as a fraction of the table.
    public static Ubo BuildLayerRenderInfo(int layer)
    {
        var block = new UboWriter(RenderInfo);
        block.Set(3, 3, layer / (float)(ResR - 1));
        return block.ToUbo("sky_renderinfo");
    }

    // The palette's phase asymmetry and amplifiers scale the bake's Rayleigh and Mie terms, and its sun colour is the light being
    // scattered.
    public static Ubo BuildBakeRenderInfo(SkyPostFx postfx, Vector3 sunWorld, in SkyLook look)
    {
        var block = new UboWriter(RenderInfo);
        block.SetAt(0, BakeRenderInfoCapture);
        SkyPostFxBlocks.WriteScattering(block, postfx, sunWorld);
        block.SetXyz(1, new Vector3(look.MieAsymmetry, look.RayleighAmplifier, look.MieAmplifier));
        block.SetXyz(3, look.SunRadiance);
        block.Set(6, postfx.GroundColor, 1f);
        return block.ToUbo("sky_bake_renderinfo");
    }

    internal static (float Dmin, float Dmax, float Dminp, float Dmaxp) SliceGeometry(int layer)
    {
        float t = layer / (float)(ResR - 1);
        t *= t;
        float r = MathF.Sqrt(Rg * Rg + t * (Rt * Rt - Rg * Rg))
                  + (layer == 0 ? 0.01f : layer == ResR - 1 ? -0.001f : 0f);

        float horizon = MathF.Sqrt(MathF.Max(0f, r * r - Rg * Rg));
        float topHorizon = MathF.Sqrt(MathF.Max(0f, Rt * Rt - Rg * Rg));
        return (Rt - r, horizon + topHorizon, r - Rg, horizon);
    }

    // (width, height, 1/width, 1/height).
    static void WriteDimensions(UboWriter block, int slot, int width, int height) =>
        block.Set(slot, width, height, 1f / width, 1f / height);

    // (n, 1/n, 1/(n-1), 1/(n/2 - 1)): texel-centre remap constants for a full and a half-resolution walk.
    static void WriteAxis(UboWriter block, int slot, int n) =>
        block.Set(slot, n, 1f / n, 1f / (n - 1), 1f / (n / 2 - 1));
}
