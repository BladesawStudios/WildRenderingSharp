using WildRenderingSharp.Graphics;
using WildRenderingSharp.Graphics.Data;
using WildRenderingSharp.Graphics.Ubos;
using WildRenderingSharp.Pipeline.Gpu;
using WildRenderingSharp.Profiles.Totk.Ubos;

namespace WildRenderingSharp.Profiles.Totk.Ubos;

/// <summary>Fills TotK's camera block from a camera.</summary>
static class TotkCameraUniforms
{
    public static Ubo Build(string key, in CameraData camera) => Context(camera).ToUbo(key);

    // The camera block of the field programs, whose frame is a grid of one tile.
    public static Ubo BuildField(in CameraData camera) => BuildTiled(TotkUniformKeys.FieldCamera, camera, 1, 1);

    // A camera block for a frame drawn as a grid of tiles, which field_hybrid's vertex stage draws as one quad per instance.
    internal static Ubo BuildTiled(string key, in CameraData camera, int columns, int rows)
    {
        var block = Context(camera);
        block.SetInt(TotkContextLayout.ScreenSize, 2, columns);
        block.SetInt(TotkContextLayout.ScreenSize, 3, rows);
        return block.ToUbo(key);
    }

    static UboWriter Context(in CameraData camera)
    {
        var block = new UboWriter(TotkContextLayout.Spec);
        GsysContext.WriteCamera(block, camera);

        // The screen size in pixels, as floats in .xy and as integers in .zw, for shaders that index a pixel as y * width + x.
        int width = (int)(1f / camera.TexelSize.X), height = (int)(1f / camera.TexelSize.Y);
        block.Set(TotkContextLayout.ScreenSize, 1f / camera.TexelSize.X, 1f / camera.TexelSize.Y, 0f, 0f);
        block.SetInt(TotkContextLayout.ScreenSize, 2, width);
        block.SetInt(TotkContextLayout.ScreenSize, 3, height);

        // The deferred vertex shader turns this into a full-screen quad from a four-vertex strip.
        block.Set(TotkContextLayout.FullscreenQuadParams, 1f, 1f, 0.5f, 0f);
        return block;
    }
}
