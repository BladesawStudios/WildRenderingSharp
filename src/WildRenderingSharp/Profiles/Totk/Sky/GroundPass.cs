using System.Numerics;
using Silk.NET.OpenGL;
using WildRenderingSharp.Graphics;
using WildRenderingSharp.Pipeline;

namespace WildRenderingSharp.Profiles.Totk.Sky;

/// <summary>Fills the view below the horizon with one colour, fading in over the haze just under it; the sky's table has nothing meaningful down there.</summary>
public sealed class GroundPass : IDisposable
{
    static readonly string FragmentSource = GlslFiles.Load("Totk/Sky/Ground/Main.frag");

    readonly GL _gl;
    readonly uint _program;

    public GroundPass(GL gl)
    {
        _gl = gl;
        _program = GLProgramBuilder.Build(gl, FullscreenShaders.Vertex330, FragmentSource, "sky_ground");
    }

    public void Run(GLResourceCache resources, RenderTargets targets, GpuTexture target, ReadOnlySpan<Vector4> viewInv3Rows,
        float aspect, float tanHalfFovY, Vector3 colour)
    {
        targets.BindColorTarget(target);
        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.CullFace);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFuncSeparate(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha, GLEnum.Zero, GLEnum.One);

        _gl.UseProgram(_program);
        // Column-major.
        Vector4 r0 = viewInv3Rows[0], r1 = viewInv3Rows[1], r2 = viewInv3Rows[2];
        Span<float> m = stackalloc float[9]
        {
            r0.X, r1.X, r2.X,
            r0.Y, r1.Y, r2.Y,
            r0.Z, r1.Z, r2.Z,
        };
        int loc = _gl.GetUniformLocation(_program, "uViewInv");
        if (loc >= 0) _gl.UniformMatrix3(loc, 1, false, m);
        _gl.SetVec2(_program, "uTanHalf", new Vector2(aspect * tanHalfFovY, tanHalfFovY));
        _gl.SetVec3(_program, "uGround", colour);

        resources.DrawFullscreenTriangle();
        _gl.Disable(EnableCap.Blend);
    }

    public void Dispose()
    {
        if (_program != 0) _gl.DeleteProgram(_program);
    }
}
