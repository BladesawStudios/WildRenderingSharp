using System.Numerics;
using WildRenderingSharp.Graphics.Ubos;
using WildRenderingSharp.Pipeline.Shadows;
using WildRenderingSharp.Pipeline.Targets;
using WildRenderingSharp.Profiles.Totk.Atmosphere;
using static WildRenderingSharp.Profiles.Totk.Ubos.TotkEnvironmentLayout;

namespace WildRenderingSharp.Profiles.Totk.Ubos;

/// <summary>
/// The slots of <c>gsys_environment</c> the game's pre-shading passes read to sample the shadow cascades, in the values that leave
/// the surface as the cascades alone describe it: no height or Depths darkness, and the shadow pre-pass's far fade of 400 to 500 metres.
/// </summary>
static class TotkPreShadingEnvironment
{
    // The depth a shadow lookup is compared at less than the surface's own, in the cascade's depth range.
    public const float DepthBias = 0.0005f;

    // The tangent offset at grazing light, which keeps a surface from shadowing itself.
    const float GrazingOffsetMetres = 0.3f;

    // The share of the shadow normal that comes from the G-buffer, against the one derived from depth, and the exponent on the grazing falloff.
    const float GBufferNormalShare = 1f, GrazingExponent = 2f;

    // The scene's environment block with these slots set, kept apart so nothing else that reads the block sees them.
    public static Ubo From(ReadOnlyMemory<byte> environment, string key, PreShadingFog fog, Vector3 entranceSky, Vector3 entranceGround, DungeonEntranceParam entrance, int cascadeSize)
    {
        var block = new UboWriter(Spec, environment.Span);
        Write(block, cascadeSize);
        WriteFog(block, fog);
        WriteEntrance(block, entranceSky, entranceGround, entrance);
        WriteVolumeFog(block, fog);
        return block.ToUbo(key);
    }

    // Each fog is a ramp over view distance as scale and bias, then its shape. The scatter fog's amount is the density times one minus (1 - ramp) raised to
    // the attenuation, and the table it samples is indexed by the same ramp raised to the horizontal exponent; the ad hoc fog's is one minus (1 - ramp) raised
    // to its ground attenuation, less a share toward the zenith.
    // TODO: the palette's YFog (YFogColor, YFogStart) is not wired: it is not one of these two, and its slots have not been found.
    static void WriteFog(UboWriter block, PreShadingFog fog)
    {
        float scatterScale = 1f / MathF.Max(fog.ScatterFar - fog.ScatterNear, 1f);
        block.Set(ScatterFogRamp, scatterScale, fog.ScatterNear * scatterScale, 0f, 0f);
        block.Set(ScatterFogShape, fog.ScatterAttenuation, fog.ScatterHorizontal, fog.ScatterDensity, 0f);

        float groundScale = 1f / MathF.Max(fog.GroundEnd - fog.GroundStart, 1f);
        block.Set(AdhocFogRamp, groundScale, fog.GroundStart * groundScale, 0f, 0f);
        block.Set(AdhocFogShape, fog.GroundAttenuation, 0f, fog.Haze.AttenSky, 1f - fog.Haze.ZenithScale);
        block.Set(AdhocFogColor, fog.Haze.Color.X, fog.Haze.Color.Y, fog.Haze.Color.Z, fog.Haze.Density);

        // The leaf pass blends its fog colour to the sky's behind the leaf over this range, and wholly replaces the leaf with it at the end of it. Unset, the
        // range is empty and every leaf is already at the end of it, which is the sky's colour: black, since nothing here supplies one. Its real values are
        // not known, so the range is the scatter fog's, far past where a leaf can be seen, and the same with the fog off.
        block.Set(LeafFogRamp, 0f, 0f, fog.LeafFogEnd, 0f);
    }

    // The low fog (YFog), which the programs weigh by the volume mask: a ramp over view distance, with the colour, a density, and an exponent on the ramp.
    // The ramp's bias is added there, where the other fogs' is taken away.
    static void WriteVolumeFog(UboWriter block, PreShadingFog fog)
    {
        float scale = 1f / MathF.Max(fog.VolumeEnd - fog.VolumeStart, 1f);
        block.Set(WorldFogMaskColor, fog.VolumeColour.X, fog.VolumeColour.Y, fog.VolumeColour.Z, fog.VolumeDensity);
        block.Set(WorldFogMaskDir, 0f, 0f, 0f, -fog.VolumeStart * scale);
        block.Set(WorldFogMaskStartEndInvDamp, scale, 1f, 0f, 0f);
    }

    // The entrance pass lights from the two colours the game gives it (DungeonEntranceParam), not from the scene's ambient, and hazes with the param's fog: its colour with
    // the strength in alpha, then the ramp's bias and scale over the start and end distances, and the damp, as the game writes them.
    static void WriteEntrance(UboWriter block, Vector3 sky, Vector3 ground, DungeonEntranceParam param)
    {
        block.Set(EntranceSkyLight, sky.X, sky.Y, sky.Z, 0f);
        block.Set(EntranceGroundLight, ground.X, ground.Y, ground.Z, 0f);

        float range = param.FogEnd - param.FogStart;
        float scale = range == 0f ? 1f : 1f / range;
        block.Set(EntranceHazeColor, param.FogColor.X, param.FogColor.Y, param.FogColor.Z, param.FogColor.W);
        block.Set(EntranceHazeShape, -param.FogStart * scale, scale, param.FogDamp, 0f);
    }

    static void Write(UboWriter block, int cascadeSize)
    {
        block.Set(HeightFade, 0f, 0f, 0f, 0f);
        block.Set(DarknessMapTransform, 0f, 0f, 0f, 0f);
        block.Set(DarknessBlend, 1f, 0f, 0f, 0f);
        float fadeScale = 1f / (SunShadowFade.End - SunShadowFade.Start), fadeBias = -SunShadowFade.Start * fadeScale;
        block.Set(ShadowFarFade, fadeScale, fadeBias, fadeScale, fadeBias);

        block.Set(ShadowDepthBias, 0, GrazingOffsetMetres);
        block.Set(ShadowDepthBias, 1, DepthBias);
        float texel = 1f / cascadeSize;
        block.Set(ShadowTexel, 0, texel);
        block.Set(ShadowTexel, 1, texel);
        block.Set(ShadowNormalBlend, 0, GBufferNormalShare);
        block.Set(ShadowNormalBlend, 1, 1f - GBufferNormalShare);
        block.Set(ShadowNormalBlend, 2, GrazingExponent);
    }
}
