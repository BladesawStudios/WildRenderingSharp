using System.Numerics;
using WildRenderingSharp.Graphics.Data;
using WildRenderingSharp.Graphics.Ubos;

namespace WildRenderingSharp.Profiles.Botw.Ubos;

/// <summary>Fills the uniform blocks BotW's shaders read.</summary>
static class BotwUniforms
{
    public static IReadOnlyList<UboSpec> InstancedBlocks { get; } = [BotwBlocks.Bones];

    public static Ubo Camera(string key, in CameraData camera) => Context(camera).ToUbo(key);

    // The camera block with the shadow cascade the pre-shading passes read: its view-to-shadow matrix, and the depths where the next
    // cascade takes over.
    public static Ubo CascadeCamera(string key, in CameraData camera, Matrix4x4 viewToShadow, float firstSplit, float secondSplit)
    {
        var block = Context(camera);
        block.Set(BotwBlocks.CascadeMatrix, viewToShadow);
        block.Set(BotwBlocks.CascadeSplits, firstSplit, secondSplit, secondSplit, secondSplit);
        return block.ToUbo(key);
    }

    public static Ubo Environment(Vector3 sunDirView, Vector3 sunColor, Vector3 hemiSky, Vector3 hemiGround, Matrix4x4 viewToWorld, float cascadeTexel)
    {
        var block = new UboWriter(BotwBlocks.Environment);
        GsysEnvironment.WriteLights(block, sunDirView, sunColor, hemiSky, hemiGround);
        block.Set(GsysEnvironment.HemiDirection, Vector3.UnitY, 0f);
        block.Set(BotwBlocks.ViewToWorld, viewToWorld);

        // The shadows fade out beyond the distance in .z and .w, which is set past anything drawn.
        block.Set(BotwBlocks.CascadeTexel, cascadeTexel, cascadeTexel, 100000f, 100000f);
        foreach (var (slot, component) in BotwBlocks.ExponentSlots)
            block.Set(slot, component, 1f);
        return block.ToUbo(FrameUniformKeys.Environment);
    }

    // The environment block is not here: it needs the shadow cascade's texel and the camera, which the lighting stage has.
    public static IReadOnlyList<Ubo> Lighting(in SceneLightingData lighting) => [SceneMaterial(lighting)];

    public static IReadOnlyList<Ubo> Actor(in SkinningData actor) => [BonePalette.For(BotwBlocks.Bones, actor)];

    static Ubo SceneMaterial(in SceneLightingData lighting)
    {
        var block = new UboWriter(BotwBlocks.SceneMaterial);
        GsysSceneMaterial.WriteLighting(block, lighting.MidScale, lighting.HighlightScale);
        block.SetAt(BotwBlocks.ProcDiscardScales, 0.1f, 10f, 0.25f);
        return block.ToUbo(FrameUniformKeys.SceneMaterial);
    }

    static UboWriter Context(in CameraData camera)
    {
        var block = new UboWriter(BotwBlocks.Context);
        GsysContext.WriteCamera(block, camera);
        return block;
    }
}
