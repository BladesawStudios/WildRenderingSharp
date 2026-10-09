using WildRenderingSharp.Graphics;
using WildRenderingSharp.Profiles.Botw.Ubos;

namespace WildRenderingSharp.Profiles.Botw;

static class BotwUniforms
{
    public static UniformBlock Camera(string key, in CameraData camera) => UniformBlock.From(key, Context(camera));

    public static BotwContextUbo Context(in CameraData camera) => BotwContextUbo.ForCamera(
        CameraData.Rows(camera.View, 3), CameraData.Rows(camera.ViewProj), CameraData.Rows(camera.Proj),
        CameraData.Rows(camera.ViewInv, 3),
        camera.Aspect, camera.TanHalfFovY, camera.Near, camera.Far, camera.TexelSize);

    public static IReadOnlyList<UniformBlock> Actor(in SkinningData actor) =>
        [UniformBlock.From(FrameUniformKeys.Bones, BonePaletteUbo.For(actor, BotwBindings.Bones))];

    public static IReadOnlyList<UniformBlock> Lighting(in SceneLightingData lighting) =>
    [
        UniformBlock.From(FrameUniformKeys.Environment, BotwEnvUbo.From(lighting.SunDirView, lighting.SunColor, lighting.HemiSky, lighting.HemiGround)),
        UniformBlock.From(FrameUniformKeys.SceneMaterial, BotwSceneMatUbo.From(lighting.MidScale, lighting.HighlightScale)),
    ];

    public static IReadOnlyList<UniformBlock> Placeholders { get; } = [UniformBlock.Zeroed(BotwBindings.Bones)];
}
