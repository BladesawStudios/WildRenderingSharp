using System.Numerics;
using System.Runtime.InteropServices;
using WildRenderingSharp.Graphics;
using WildRenderingSharp.Profiles.Botw.Ubos;
using WildRenderingSharp.Profiles.Totk.Ubos;
using WildRenderingSharp.Rendering;

namespace WildRenderingSharp.Profiles.Botw;

static class BotwUniforms
{
    public static UniformBlock Camera(string key, in CameraData camera) => UniformBlock.From(key, Context(camera));

    public static BotwContextUbo Context(in CameraData camera) => BotwContextUbo.ForCamera(
        CameraData.Rows(camera.View, 3), CameraData.Rows(camera.ViewProj), CameraData.Rows(camera.Proj),
        CameraData.Rows(camera.ViewInv, 3),
        camera.Aspect, camera.TanHalfFovY, camera.Near, camera.Far, camera.TexelSize);

    public static IReadOnlyList<UniformBlock> Actor(in SkinningData actor)
    {
        Vector4[] placement = actor.PlacementRows;
        var skeleton = actor.Skeleton;
        BonePaletteUbo bones = skeleton is null
            ? BonePaletteUbo.FillIdentity(placement)
            : BonePaletteUbo.Build(actor.BoneWorld ?? SkeletonPose.BindPoseWorldMatrices(skeleton),
                CollectionsMarshal.AsSpan(skeleton.MatrixToBoneList), skeleton.InverseModelMatricesAsMatrices(), CameraData.FromRows(placement));
        return [UniformBlock.From(FrameUniformKeys.Bones, bones)];
    }

    public static IReadOnlyList<UniformBlock> Lighting(in SceneLightingData lighting) =>
    [
        UniformBlock.From(FrameUniformKeys.Environment, BotwEnvUbo.From(lighting.SunDirView, lighting.SunColor, lighting.HemiSky, lighting.HemiGround)),
        UniformBlock.From(FrameUniformKeys.SceneMaterial, BotwSceneMatUbo.From(lighting.MidScale, lighting.HighlightScale)),
    ];

    public static IReadOnlyList<UniformBlock> Placeholders { get; } = [UniformBlock.Zeroed(BotwBindings.Bones)];
}
