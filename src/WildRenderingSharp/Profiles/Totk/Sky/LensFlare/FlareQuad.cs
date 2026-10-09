using Silk.NET.OpenGL;

namespace WildRenderingSharp.Profiles.Totk.Sky.LensFlare;

/// <summary>The quad the flare program draws: a half-unit position (its vertex shader doubles it) and a uv per corner.</summary>
sealed unsafe class FlareQuad : IDisposable
{
    readonly GL _gl;
    readonly uint _vao, _vbo;

    public FlareQuad(GL gl)
    {
        _gl = gl;
        Span<float> corners =
        [
            -0.5f, -0.5f, 0f, 0f,
             0.5f, -0.5f, 1f, 0f,
            -0.5f,  0.5f, 0f, 1f,
             0.5f,  0.5f, 1f, 1f,
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
