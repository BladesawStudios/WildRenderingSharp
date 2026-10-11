using System.Numerics;
using WildRenderingSharp.Graphics.Ubos;
using WildRenderingSharp.Profiles.Totk.Atmosphere.Palettes;

namespace WildRenderingSharp.Profiles.Totk.Sky.PostFx;

/// <summary>The uniform blocks of <c>agl_sky_postfx_sky</c>: Context, with the view-ray basis, and RenderInfo, with the atmosphere.</summary>
static class SkyPostFxBlocks
{
    public static readonly UboSpec Context = new("Context", 25, 224);
    public static readonly UboSpec RenderInfo = new("RenderInfo", 26, 112);

    public static Ubo BuildContext(ReadOnlySpan<Vector4> viewInv3Rows, float tanHalfFovX, float tanHalfFovY, float intensity,
        SkyPostFxPass.AdhocFog fog = default)
    {
        var block = new UboWriter(Context);

        // The view ray is (ndc.x * [0].x, ndc.y * [1].y, -1), rotated to the world by the camera's rows.
        block.Set(0, 0, tanHalfFovX);
        block.Set(1, 1, tanHalfFovY);
        block.Set(2, 3, -1f);
        for (int row = 0; row < 3; row++)
            block.SetXyz(4 + row, new Vector3(viewInv3Rows[row].X, viewInv3Rows[row].Y, viewInv3Rows[row].Z));
        block.Set(13, 0, intensity);

        // The adhoc fog's haze band, in slots 10 and 11; see docs/uniform_blocks.md.
        block.Set(10, 1, fog.AttenSky);
        block.Set(10, 2, fog.ZenithScale);
        block.Set(10, 3, fog.Density);
        block.SetXyz(11, fog.Color);
        return block.ToUbo("skyfx_context");
    }

    public static Ubo BuildRenderInfo(SkyPostFx postfx, Vector3 sunWorld, Vector3? fogColor = null, float paletteTint = 0f)
    {
        var block = new UboWriter(RenderInfo);
        WriteScattering(block, postfx, sunWorld);

        // The palette's FogColor when it has one, so the colour the table blends toward tracks the palette. [6].w is the blend bias the
        // shader clamps against the table's alpha: 1 pins the table, and lowering it lets the palette's colour through by that amount.
        block.SetXyz(6, fogColor ?? postfx.GroundColor);
        block.Set(6, 3, 1f - Math.Clamp(paletteTint, 0f, 1f));
        return block.ToUbo("skyfx_renderinfo");
    }

    // The Rayleigh and Mie coefficients in slot 0 and the unit sun direction in slot 2, which the post-fx and the bake both read.
    internal static void WriteScattering(UboWriter block, SkyPostFx postfx, Vector3 sunWorld)
    {
        var rayleigh = postfx.RayleighScatteringCoeff;
        block.Set(0, rayleigh.X, rayleigh.Y, rayleigh.Z, postfx.MieScatteringCoeff);
        block.SetXyz(2, sunWorld.LengthSquared() > 1e-12f ? Vector3.Normalize(sunWorld) : sunWorld);
    }
}
