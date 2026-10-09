using WildRenderingSharp.Gpu;
using WildRenderingSharp.Pipeline.Targets;
using WildRenderingSharp.Shaders;
using System.Numerics;
using Silk.NET.OpenGL;

namespace WildRenderingSharp.Pipeline.Passes;

/// <summary>
/// Blender-style ground reference grid: this renderer's own small utility shader (not a decompiled game shader), a single large
/// quad at world Y=0 with a procedural, distance-faded, anti-aliased pattern.
/// </summary>
public sealed class GridPass : IDisposable
{
    readonly GL _gl;
    readonly uint _program;
    readonly uint _vao, _vbo;

    static readonly string VertexSource = GlslFiles.Load("Pipeline/Grid/Main.vert");

    static readonly string FragmentSource = GlslFiles.Load("Pipeline/Grid/Main.frag");

    public unsafe GridPass(GL gl)
    {
        _gl = gl;
        _program = GLProgramBuilder.Build(gl, VertexSource, FragmentSource, "grid");

        // A single [-1,1] quad, two triangles - the shader scales it to world size.
        float[] verts = [-1f, -1f, 1f, -1f, 1f, 1f, -1f, -1f, 1f, 1f, -1f, 1f];
        _vao = gl.GenVertexArray();
        _vbo = gl.GenBuffer();
        gl.BindVertexArray(_vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        fixed (float* p = verts)
            gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(verts.Length * sizeof(float)), p, BufferUsageARB.StaticDraw);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), (void*)0);
        gl.BindVertexArray(0);
    }

    public unsafe void Run(RenderTargets targets, Matrix4x4 viewProj, Vector3 cameraPos, float extent)
    {
        targets.BindColorAndDepthTarget(targets.Scene, targets.GBufferDepth);
        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Less);
        _gl.DepthMask(false);
        _gl.Disable(EnableCap.CullFace);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFuncSeparate(GLEnum.SrcAlpha, GLEnum.OneMinusSrcAlpha, GLEnum.One, GLEnum.Zero);
        _gl.BlendEquationSeparate(GLEnum.FuncAdd, GLEnum.FuncAdd);

        _gl.UseProgram(_program);
        _gl.SetMat4(_program, "uViewProj", viewProj);
        _gl.Uniform1(_gl.GetUniformLocation(_program, "uExtent"), extent);
        _gl.Uniform1(_gl.GetUniformLocation(_program, "uMinorCell"), MinorCellFor(extent));
        _gl.Uniform3(_gl.GetUniformLocation(_program, "uCameraPos"), cameraPos.X, cameraPos.Y, cameraPos.Z);
        _gl.SetVec4(_program, "uLineColor", new Vector4(0.55f, 0.58f, 0.63f, 0.6f));

        _gl.BindVertexArray(_vao);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 6);

        _gl.DepthMask(true);
        _gl.Disable(EnableCap.Blend);
    }

    // The minor grid cell's world size for a quad extent: a power of ten chosen to keep the number of cells across the plane
    // roughly constant whatever the model's scale.
    internal static float MinorCellFor(float extent) =>
        MathF.Max(1e-3f, MathF.Pow(10f, MathF.Floor(MathF.Log10(MathF.Max(extent, 1e-3f) / 20f))));

    public void Dispose()
    {
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteVertexArray(_vao);
        _gl.DeleteProgram(_program);
    }
}
