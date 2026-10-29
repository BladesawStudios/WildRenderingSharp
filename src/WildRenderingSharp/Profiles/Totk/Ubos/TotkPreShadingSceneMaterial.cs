using System.Numerics;
using WildRenderingSharp.Graphics.Ubos;
using static WildRenderingSharp.Profiles.Totk.Ubos.TotkSceneMaterialLayout;

namespace WildRenderingSharp.Profiles.Totk.Ubos;

/// <summary>
/// The first slots of the scene-shading array that <c>preshading_field</c> reads to light the surface from the sky and the ground: how much of the
/// ambient comes from the cube map, whose share is zero within 80 m of the camera otherwise, and the two colours it fades to beyond that, which are the
/// ones the rest of the frame is lit with.
/// </summary>
static class TotkPreShadingSceneMaterial
{
    // Entries of the array, in vec4 slots from its start.
    const int CubeAmbientSlot = 1, SkySlot = 2, GroundSlot = 3, SlotBytes = 16;

    // The scene's block with these slots set, kept apart so nothing else that reads the block sees them.
    public static Ubo From(ReadOnlyMemory<byte> sceneMaterial, string key, Vector3 sky, Vector3 ground, float shadowFilterTexels)
    {
        var block = new UboWriter(Spec, sceneMaterial.Span);

        // The width of the filter the character shadows are read through, in cascade texels: the game steps four taps this far from the point, each of them already smoothed.
        block.SetAt(ConstDepthShadowCharaPcf, shadowFilterTexels);
        block.SetAt(SceneShadingInfoExposureBase + CubeAmbientSlot * SlotBytes, 1f, 1f, 1f, 0f);
        block.SetAt(SceneShadingInfoExposureBase + SkySlot * SlotBytes, sky.X, sky.Y, sky.Z, 0f);
        block.SetAt(SceneShadingInfoExposureBase + GroundSlot * SlotBytes, ground.X, ground.Y, ground.Z, 0f);
        return block.ToUbo(key);
    }
}
