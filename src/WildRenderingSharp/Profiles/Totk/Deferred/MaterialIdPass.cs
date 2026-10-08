using Silk.NET.OpenGL;
using WildRenderingSharp.Graphics;
using WildRenderingSharp.Pipeline;

namespace WildRenderingSharp.Profiles.Totk.Deferred;

/// <summary>
/// Stamps the pass-ID mask from the material IDs the G-buffer programs wrote, the game's own way of choosing which resolve pass lights a pixel
/// (see <see cref="DeferredPassPriorities"/>), over what <see cref="PassIdMaskPass"/> stamped from each shape's name. A character's cloth the
/// exporter names after its <c>o_material_behave</c> is lit by whichever pass its G-buffer program says, which can be another one.
/// </summary>
public sealed class MaterialIdPass : IDisposable
{
    public const int MaxPasses = 16;

    static readonly string FragmentSource = GlslFiles.Load("Totk/Deferred/MaterialId/Main.frag");

    readonly GL _gl;
    readonly uint _program;

    public MaterialIdPass(GL gl)
    {
        _gl = gl;
        _program = GLProgramBuilder.Build(gl, FullscreenShaders.Vertex450, FragmentSource, "material_id");
    }

    /// <param name="passes">Each pass's priority and its index in the pass list; its pass-ID is that index plus one over 255.</param>
    public unsafe void Run(GLResourceCache resources, RenderTargets targets, IReadOnlyList<(int Priority, int PassIndex)> passes)
    {
        if (passes.Count == 0)
            return;

        int count = Math.Min(passes.Count, MaxPasses);
        int* priorities = stackalloc int[MaxPasses];
        float* ids = stackalloc float[MaxPasses];
        for (int i = 0; i < count; i++)
        {
            priorities[i] = passes[i].Priority;
            ids[i] = (passes[i].PassIndex + 1) / 255f;
        }

        targets.BindPassIdTarget();
        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.Blend);
        _gl.Disable(EnableCap.CullFace);
        _gl.DepthMask(false);

        _gl.UseProgram(_program);
        _gl.BindTextureUniform(_program, "tex_material_id", 0, targets.GBuffer[0].Handle);
        _gl.BindTextureUniform(_program, "tex_gbuf_depth", 1, targets.GBufferDepth.Handle);
        _gl.SetInt(_program, "uCount", count);
        _gl.Uniform1(_gl.GetUniformLocation(_program, "uPriority"), (uint)count, priorities);
        _gl.Uniform1(_gl.GetUniformLocation(_program, "uPassId"), (uint)count, ids);
        resources.DrawFullscreenTriangle();
        _gl.DepthMask(true);
    }

    public void Dispose() => _gl.DeleteProgram(_program);
}
