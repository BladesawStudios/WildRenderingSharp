using WildRenderingSharp.Gpu;
using WildRenderingSharp.Pipeline.Resources;
using WildRenderingSharp.Shaders;
using System.Numerics;
using Silk.NET.OpenGL;

namespace WildRenderingSharp.Pipeline.Passes;

/// <summary>
/// The final blit: box-downsamples the supersampled render in linear light (filtering after the sRGB encode would darken edges),
/// applies <c>agl</c>'s colour-correction curve (hue, saturation, brightness, gamma) and sRGB-encodes.
/// </summary>
public sealed class PresentPass : IDisposable
{
    readonly GL _gl;
    readonly uint _program;

    static readonly string FragmentSource = GlslFiles.Load("Pipeline/Present/Main.frag");

    public PresentPass(GL gl)
    {
        _gl = gl;
        _program = GLProgramBuilder.Build(gl, FullscreenShaders.Vertex450, FragmentSource, "present_blit");
    }

    public void Run(GLResourceCache resources, GpuTexture source, int supersample, float saturation, float brightness, float gamma, bool hdrPreview = false, GpuTexture? alphaSource = null)
    {
        _gl.UseProgram(_program);
        _gl.BindTextureUniform(_program, "t", 0, source.Handle);
        _gl.SetInt(_program, "uMode", hdrPreview ? 1 : 0);
        _gl.SetInt(_program, "uSS", Math.Max(1, supersample));
        _gl.SetVec2(_program, "uTexel", new Vector2(1f / source.Width, 1f / source.Height));
        _gl.SetFloat(_program, "uSaturation", saturation);
        _gl.SetFloat(_program, "uBrightness", brightness);
        _gl.SetFloat(_program, "uGamma", gamma);
        BindAlpha(alphaSource);
        resources.DrawFullscreenTriangle();
    }

    public void RunRaw(GLResourceCache resources, GpuTexture source, float scale, GpuTexture? alphaSource = null)
    {
        _gl.UseProgram(_program);
        _gl.BindTextureUniform(_program, "t", 0, source.Handle);
        _gl.SetInt(_program, "uMode", 2);
        _gl.SetFloat(_program, "uRawScale", scale);
        BindAlpha(alphaSource);
        resources.DrawFullscreenTriangle();
    }

    // Binds a real alpha source (see Run) at a unit distinct from t. Always bound to something valid, falling back to unit 0's
    // texture, so tAlpha never points at an incomplete texture whatever uUseAlpha says.
    void BindAlpha(GpuTexture? alphaSource)
    {
        _gl.BindTextureUniform(_program, "tAlpha", 1, (alphaSource ?? default).Handle);
        _gl.SetInt(_program, "uUseAlpha", alphaSource is null ? 0 : 1);
    }

    public void Dispose() => _gl.DeleteProgram(_program);
}
