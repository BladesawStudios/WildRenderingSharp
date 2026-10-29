using System.Numerics;
using Silk.NET.OpenGL;
using WildRenderingSharp;
using WildRenderingSharp.Profiles.Totk.Gloom;

namespace WildRenderingSharp.TestBench;

/// <summary>A gloom of one box around the camera's target and flat noise, to run the game's gloom programs on a scene and count the pixels they claim.</summary>
sealed unsafe class GloomBench : IGloomSource
{
    public GloomTextures? Textures { get; }

    public GloomLook Look { get; } = new(
        new(20f, 300f, 1f, 0.2f), new(0.35f, 0.04f, 0.1f, 0.5f), new(0.6f, 0.05f, 0.1f), new(0.05f, 0f, 0.02f), new(1f, 2f),
        new(0.1f, 0.2f), new(0.2f, 0f, 0f), new(2f, 0.1f), new(0.3f, 0.5f, 0.2f));

    public IReadOnlyList<GloomVolume> Volumes { get; }

    public float? AltitudeDensity => 0.6f;

    // The material ID the compose program writes for the gloom is 32; counts the pixels of the last frame's G-buffer that carry it.
    public static void Report(WildRenderer renderer)
    {
        var targets = renderer.View.Targets;
        byte[] ids = targets.ReadPixelsRgba8(targets.GBuffer[0]);
        int claimed = Enumerable.Range(0, ids.Length / 4).Count(i => ids[i * 4] == 32);
        Console.WriteLine($"gloom: composed={renderer.Pipeline.GloomComposed}, material-ID-32 pixels {claimed} of {ids.Length / 4}");
    }

    public GloomBench(GL gl, Vector3 centre, float size)
    {
        uint noise = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture3D, noise);
        byte half = 128;
        gl.TexImage3D(TextureTarget.Texture3D, 0, InternalFormat.R8, 1, 1, 1, 0, PixelFormat.Red, PixelType.UnsignedByte, &half);
        gl.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        gl.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        gl.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureWrapS, (int)GLEnum.Repeat);
        gl.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureWrapT, (int)GLEnum.Repeat);
        gl.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureWrapR, (int)GLEnum.Repeat);
        uint plane = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, plane);
        uint texel = 0x80808080u;
        gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, 1, 1, 0, PixelFormat.Rgba, PixelType.UnsignedByte, &texel);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        Textures = new GloomTextures(noise, noise, noise, noise, noise, plane, plane);

        var placement = Matrix4x4.CreateScale(size) * Matrix4x4.CreateTranslation(centre);
        Volumes = [new GloomVolume(GloomShape.Box, placement, new(0.9f, 0.9f), Vector2.One, 0f, false)];
    }
}
