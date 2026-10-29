using Silk.NET.OpenGL;

namespace WildRenderingSharp.Gpu;

/// <summary>The quad the game's screen programs draw: a half-unit position (their vertex shaders double it) and a uv per corner.</summary>
/// <remarks>
/// The uv runs up the screen, as a lower-left window origin has it. A program drawn with the game's upper-left origin reads row zero as the top of the screen, so its quad is made
/// with <c>topFirst</c>, which puts uv.y zero at the top.
/// </remarks>
sealed unsafe class HalfUnitQuad : IDisposable
{
    readonly GL _gl;
    readonly uint _vao, _vbo;

    public HalfUnitQuad(GL gl, bool topFirst = false)
    {
        _gl = gl;
        float bottom = topFirst ? 1f : 0f, top = topFirst ? 0f : 1f;
        Span<float> corners =
        [
            -0.5f, -0.5f, 0f, bottom,
             0.5f, -0.5f, 1f, bottom,
            -0.5f,  0.5f, 0f, top,
             0.5f,  0.5f, 1f, top,
        ];
        _vao = gl.GenVertexArray();
        _vbo = gl.GenBuffer();
        gl.BindVertexArray(_vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        fixed (float* p = corners)
            gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(corners.Length * sizeof(float)), p, BufferUsageARB.StaticDraw);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), (void*)0);
        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), (void*)(2 * sizeof(float)));
        gl.BindVertexArray(0);
    }

    public void Draw()
    {
        _gl.BindVertexArray(_vao);
        _gl.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
        _gl.BindVertexArray(0);
    }

    public void Dispose()
    {
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteVertexArray(_vao);
    }
}
