using Silk.NET.OpenGL;
using WildRenderingSharp.Gpu;
using WildRenderingSharp.Pipeline.Passes;
using WildRenderingSharp.Pipeline.Resources;
using WildRenderingSharp.Pipeline.Targets;
using WildRenderingSharp.Shaders;

namespace WildRenderingSharp.Profiles.Totk.Deferred.PreShading;

/// <summary>
/// Spreads the pre-shading buffers' drawn texels one texel into the sky around them, so the resolves' bilinear reads at the edge of a surface blend its own
/// values with their neighbours' rather than with what the approximation left over the sky.
/// </summary>
internal sealed class DilatePass : IDisposable
{
    static readonly string FragmentSource = GlslFiles.Load("Totk/Deferred/PreShading/Dilate.frag");

    readonly GL _gl;
    readonly uint _program;

    public DilatePass(GL gl)
    {
        _gl = gl;
        _program = GLProgramBuilder.Build(gl, FullscreenShaders.Vertex450, FragmentSource, "preshading_dilate");
    }

    public void Run(GLResourceCache resources, RenderTargets targets)
    {
        Dilate(resources, targets, targets.PreShadow);
        Dilate(resources, targets, targets.PreMisc);
        foreach (int layer in new[] { 0, 1 })
        {
            DilateLayer(resources, targets, targets.FieldLightPrePassArray, layer);
            DilateLayer(resources, targets, targets.LightPrePassArray, layer);
        }
    }

    public void Dispose() => _gl.ReleaseProgram(_program);

    void Dilate(GLResourceCache resources, RenderTargets targets, GpuTexture texture)
    {
        _gl.CopyImageSubData(texture.Handle, CopyImageSubDataTarget.Texture2D, 0, 0, 0, 0,
            targets.PreShadowScratch.Handle, CopyImageSubDataTarget.Texture2D, 0, 0, 0, 0, (uint)texture.Width, (uint)texture.Height, 1);
        targets.BindColorTarget(texture);
        Draw(resources, targets);
    }

    void DilateLayer(GLResourceCache resources, RenderTargets targets, GpuTexture array, int layer)
    {
        _gl.CopyImageSubData(array.Handle, CopyImageSubDataTarget.Texture2DArray, 0, 0, 0, layer,
            targets.PreShadowScratch.Handle, CopyImageSubDataTarget.Texture2D, 0, 0, 0, 0, (uint)array.Width, (uint)array.Height, 1);
        targets.BindColorTargetLayer(array, layer);
        Draw(resources, targets);
    }

    void Draw(GLResourceCache resources, RenderTargets targets)
    {
        _gl.UseProgram(_program);
        _gl.BindTextureUniform(_program, "tex_src", 0, targets.PreShadowScratch.Handle);
        _gl.BindTextureUniform(_program, "tex_half_depth", 1, targets.LinearDepthHalf.Handle);
        resources.DrawFullscreenTriangle();
    }
}
