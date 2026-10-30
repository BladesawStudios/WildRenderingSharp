using System.Numerics;
using WildRenderingSharp.Graphics.Ubos;
using WildRenderingSharp.Pipeline.Frame;
using WildRenderingSharp.Pipeline.Passes;
using WildRenderingSharp.Pipeline.Shadows;
using WildRenderingSharp.Profiles.Totk.Ubos;

namespace WildRenderingSharp.Profiles.Totk.Deferred.PreShading;

/// <summary>
/// The camera block the game's pre-shading passes read their shadow cascades from: a view-to-shadow-texture matrix for each cascade, and
/// the view depths at which the next cascade takes over.
/// </summary>
static class PreShadingCascades
{
    const float NoSplit = 1e9f;

    // The game's cascades are rendered with a slope-scaled offset ours lack, and its pass adds only a fixed depth bias and a short offset, so a surface the
    // sun meets at a slant shadows itself, fully, wherever its depth varies by more than that across a texel. The depth bias is therefore a number of
    // texels: a base, and more as the sun comes lower, where the ground it falls on slants more.
    const float BaseEdgeTexels = 3f, SlantEdgeTexels = 3f, MaxSlant = 8f, LowestSun = 0.05f;

    public static Ubo Camera(string key, FrameContext frame, ScreenSpaceShadowAndAoPass.CascadeParams cascades, bool field)
    {
        int count = cascades.ViewProj.Length;
        var viewToShadow = new Matrix4x4[count];
        float edgeTexels = EdgeTexels(frame.Setup.SunWorld);
        for (int c = 0; c < count; c++)
            viewToShadow[c] = frame.Setup.Cam.ViewInv * cascades.ViewProj[c] * ToTexture(cascades.Bias[c] + edgeTexels * cascades.TexelWorld[c] * cascades.InvDepthRange[c]);

        return TotkCameraUniforms.BuildCascaded(key, frame.Setup.Cam, viewToShadow, Splits(frame, count), field);
    }

    // The cotangent of the sun's height is how far the ground it falls on changes depth across a texel, in texels.
    static float EdgeTexels(Vector3 sunWorld)
    {
        float height = MathF.Max(sunWorld.Y, LowestSun);
        float cotangent = MathF.Sqrt(MathF.Max(1f - height * height, 0f)) / height;
        return BaseEdgeTexels + SlantEdgeTexels * MathF.Min(cotangent, MaxSlant);
    }

    // Clip space to texture space. The depth is also moved by the difference between the bias the environment block states and this cascade's,
    // since the shader compares at the surface depth less that bias.
    static Matrix4x4 ToTexture(float cascadeBias) =>
        Matrix4x4.CreateScale(0.5f) * Matrix4x4.CreateTranslation(0.5f, 0.5f, 0.5f + TotkPreShadingEnvironment.DepthBias - cascadeBias);

    static Vector3 Splits(FrameContext frame, int count)
    {
        Span<float> splits = [NoSplit, NoSplit, NoSplit];
        for (int c = 0; c < Math.Min(count - 1, splits.Length); c++)
            splits[c] = frame.ShadowCache.CascadeSliceEnd[c] is > 0f and var end ? end : frame.ShadowCache.CascadeRadius[c] * ShadowCascadeLayout.ReachShare;
        return new Vector3(splits[0], splits[1], splits[2]);
    }
}
