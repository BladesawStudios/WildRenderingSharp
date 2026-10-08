using WildRenderingSharp.Graphics;
using WildRenderingSharp.Profiles.Totk.Ubos;

namespace WildRenderingSharp.Profiles.Totk;

static class TotkLightingUniforms
{
    public static UniformBlock[] Build(IWorldBasis world, in SceneLightingData lighting)
    {
        var env = EnvUbo.BuildFromLighting(
            lighting.SunDirView, world.Direction(lighting.SunDirWorld), lighting.SunColor,
            lighting.HemiSky, lighting.HemiGround, lighting.VolumeMaskColor, lighting.VolumeMaskIntensity,
            lighting.ShadowMapSize);
        var sceneMaterial = SceneMatUbo.BuildFromLighting(
            lighting.HemiSky, lighting.HemiGround, lighting.MidScale, lighting.HighlightScale);

        return
        [
            new UniformBlock("env", (uint)env.BindingIndex, env.ToByteArray()),
            new UniformBlock("scenemat", (uint)sceneMaterial.BindingIndex, sceneMaterial.ToByteArray()),
        ];
    }
}
