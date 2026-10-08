using System.Numerics;
using Silk.NET.OpenGL;

namespace WildRenderingSharp.Pipeline;

/// <summary>
/// Blender-style ground reference grid: this renderer's own small utility shader (not a decompiled game shader), a single large
/// quad at world Z=0 with a procedural, distance-faded, anti-aliased pattern. Drawn into <see cref="RenderTargets.Scene"/>,
/// depth-tested against <see cref="RenderTargets.GBufferDepth"/> so opaque geometry occludes it, in the same Y-flipped space
/// <c>ForwardPass</c> and <c>KnownMaterialFixes</c> draw into, so it takes the flipped view-projection. Runs before the forward
/// pass so blended materials draw over it, with no depth write of its own.
/// </summary>
public sealed class GridPass : IDisposable
{
    readonly GL _gl;
    readonly uint _program;
    readonly uint _vao, _vbo;

    const string VertexSource = """
        #version 450 core
        layout (location = 0) in vec2 aPos; // [-1,1] quad corner, scaled/positioned in the shader
        uniform mat4 uViewProj;
        uniform float uExtent;
        out vec2 vWorldXY;
        void main()
        {
            vWorldXY = aPos * uExtent;
            gl_Position = uViewProj * vec4(vWorldXY, 0.0, 1.0);
        }
        """;

    const string FragmentSource = """
        #version 450 core
        in vec2 vWorldXY;
        uniform vec3 uCameraPos;
        uniform vec4 uLineColor;
        uniform float uExtent;
        uniform float uMinorCell;
        layout (location = 0) out vec4 fragColor;

        // One anti-aliased line set at the given world cell size, using screen-space derivatives so line thickness stays about 1px at any distance (no geometry, no texture).
        // `vis` is the anti-aliasing guard and is not optional. `deriv` is in cells per pixel, so once it reaches 1 a whole cell fits in one pixel and the line test stops meaning anything: the
        // numerator is bounded by 0.5 while the denominator keeps growing, so `line` saturates to 1 for every fragment and the grid becomes a solid sheet. That happened with 1/10-unit cells on a
        // 13,880-unit quad (Enemy_Dragon_Darkness, radius 694) whose bounds centre is below the ground, so the sheet covered the upper screen and read as the sky's colour inverted. Fading a level
        // out as its cells approach pixel size makes that impossible.
        float gridLines(vec2 p, float cell, out float vis)
        {
            vec2 coord = p / cell;
            vec2 deriv = fwidth(coord);
            vis = clamp(1.0 - max(deriv.x, deriv.y), 0.0, 1.0);
            vec2 grid = abs(fract(coord - 0.5) - 0.5) / max(deriv, 1e-6);
            return 1.0 - clamp(min(grid.x, grid.y), 0.0, 1.0);
        }

        void main()
        {
            float dist = distance(vWorldXY, uCameraPos.xy);
            float fade = clamp(1.0 - dist / uExtent, 0.0, 1.0);
            fade *= fade;

            // The cell size comes from the caller, scaled to the scene (see Run).
            float minorVis, majorVis;
            float minor = gridLines(vWorldXY, uMinorCell, minorVis) * minorVis;
            float major = gridLines(vWorldXY, uMinorCell * 10.0, majorVis) * majorVis;
            float line = max(minor * 0.35, major * 0.8);

            if (line * fade < 0.01) discard;
            fragColor = vec4(uLineColor.rgb, uLineColor.a * line * fade);
        }
        """;

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

    public unsafe void Run(RenderTargets targets, ReadOnlySpan<Vector4> viewProjRows, Vector3 cameraPos, float extent)
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
        _gl.SetMat4(_program, "uViewProj", viewProjRows);
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
