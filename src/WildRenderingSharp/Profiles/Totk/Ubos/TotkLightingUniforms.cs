using System.Numerics;
using WildRenderingSharp.Graphics;
using WildRenderingSharp.Graphics.Data;
using WildRenderingSharp.Graphics.Ubos;
using WildRenderingSharp.Pipeline.Gpu;
using WildRenderingSharp.Profiles.Totk.Ubos;
using static WildRenderingSharp.Profiles.Totk.Ubos.TotkEnvironmentLayout;

namespace WildRenderingSharp.Profiles.Totk.Ubos;

/// <summary>Fills TotK's environment and scene-material blocks from the scene's lighting.</summary>
static class TotkLightingUniforms
{
    public static Ubo[] Build(in SceneLightingData lighting) => [Environment(lighting), SceneMaterial(lighting)];

    static Ubo Environment(in SceneLightingData lighting)
    {
        var block = new UboWriter(Spec);
        GsysEnvironment.WriteLights(block, lighting.SunDirView, lighting.SunColor, lighting.HemiSky, lighting.HemiGround);

        // The shaders read only LightDir0World, to walk a shading point back along the light's travel direction.
        block.Set(HemiDirWorld, Vector3.UnitY, 0f);
        block.Set(LightDir0World, -lighting.SunDirWorld, 0f);
        block.Set(LightDir1World, 0f, -1f, 0f, 0f);

        // The four fog groups stay zero, since a placeholder in them skews some forward materials toward cyan.
        // Height attenuation of ambient is a no-op at z = 1 for any height.
        block.Set(AmbientHeightAttenuation, 0f, 0f, 1f, 0f);
        block.Set(Unknown70, 2, 1f);
        block.Set(VolumeMaskTint, lighting.VolumeMaskColor, lighting.VolumeMaskIntensity);

        block.Set(ShadowDepthBias, 1, 0.0005f);
        block.SetInt(ShadowMapDimensions, 0, lighting.ShadowMapSize);
        block.SetInt(ShadowMapDimensions, 1, lighting.ShadowMapSize);

        foreach (var (slot, component) in PowExponentSlots)
            if (block.Get(slot, component) == 0f)
                block.Set(slot, component, 1f);
        return block.ToUbo(FrameUniformKeys.Environment);
    }

    static Ubo SceneMaterial(in SceneLightingData lighting)
    {
        var block = new UboWriter(TotkSceneMaterialLayout.Spec);
        TotkSceneMaterialDefaults.Write(block);
        GsysSceneMaterial.WriteLighting(block, lighting.MidScale, lighting.HighlightScale);

        // The ambient sky and ground pair the deferred vertex shader lerps by screen Y, and a pow() exponent, in the archive's unnamed array.
        block.SetAt(TotkSceneMaterialLayout.SceneShadingSkyAmbient, lighting.HemiSky.X, lighting.HemiSky.Y, lighting.HemiSky.Z);
        block.SetAt(TotkSceneMaterialLayout.SceneShadingGroundAmbient, lighting.HemiGround.X, lighting.HemiGround.Y, lighting.HemiGround.Z);
        block.SetAt(TotkSceneMaterialLayout.SceneShadingPowExponent, 1f);
        return block.ToUbo(FrameUniformKeys.SceneMaterial);
    }
}
