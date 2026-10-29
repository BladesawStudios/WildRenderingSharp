using Silk.NET.OpenGL;

namespace WildRenderingSharp.Profiles.Totk.Gloom;

/// <summary>The unit box the shape programs' vertex stage places: a corner position per vertex, a half on every side, drawn as 36 vertices.</summary>
sealed unsafe class GloomCube : IDisposable
{
    const int VertexCount = 36;

    // Each face as two triangles; which way they wind does not matter, as the passes draw the box with no culling.
    static readonly int[] Corners =
    [
        0, 1, 3, 0, 3, 2, 4, 6, 7, 4, 7, 5,
        0, 4, 5, 0, 5, 1, 2, 3, 7, 2, 7, 6,
        0, 2, 6, 0, 6, 4, 1, 5, 7, 1, 7, 3,
    ];

    readonly GL _gl;
    readonly uint _vao, _vbo;

    public GloomCube(GL gl)
    {
        _gl = gl;
        var positions = new float[VertexCount * 3];
        for (int i = 0; i < VertexCount; i++)
        {
            int corner = Corners[i];
            positions[i * 3] = (corner & 1) == 0 ? -0.5f : 0.5f;
            positions[i * 3 + 1] = (corner & 2) == 0 ? -0.5f : 0.5f;
            positions[i * 3 + 2] = (corner & 4) == 0 ? -0.5f : 0.5f;
        }

        _vao = gl.GenVertexArray();
        _vbo = gl.GenBuffer();
        gl.BindVertexArray(_vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        fixed (float* p = positions)
            gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(positions.Length * sizeof(float)), p, BufferUsageARB.StaticDraw);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), (void*)0);
        gl.BindVertexArray(0);
    }

    public void Draw()
    {
        _gl.BindVertexArray(_vao);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, VertexCount);
        _gl.BindVertexArray(0);
    }

    public void Dispose()
    {
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteVertexArray(_vao);
    }
}
