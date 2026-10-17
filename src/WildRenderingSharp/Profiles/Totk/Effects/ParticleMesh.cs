using System.Numerics;
using Silk.NET.OpenGL;

namespace WildRenderingSharp.Profiles.Totk.Effects;

/// <summary>An emitter's mesh on the GPU, drawn once per particle with the attributes the game's particle programs read for each.</summary>
internal sealed class ParticleMesh : IDisposable
{
    // The attribute locations the programs declare: the vertex's, then the particle's, then the three rows of the emitter's matrix.
    const uint PositionAttribute = 0, TexCoordAttribute = 1, ColorAttribute = 3, LocalPositionAttribute = 4, LocalVectorAttribute = 5, ScaleAttribute = 6, RandomAttribute = 7,
        InitialRotationAttribute = 8, EmitterMatrixAttribute = 9;

    // Each particle holds its local position (with its life), local vector (with its birth time), scale, random numbers and initial rotation, as four floats each.
    public const int ParticleFloats = 5 * 4;

    readonly GL _gl;
    readonly uint _array, _vertices, _indices, _particles;
    readonly PrimitiveType _primitive;
    readonly uint _indexCount;

    public unsafe ParticleMesh(GL gl, ParticleEmitterAsset asset)
    {
        _gl = gl;
        _primitive = asset.Manifest.TriangleStrip ? PrimitiveType.TriangleStrip : PrimitiveType.Triangles;
        _indexCount = (uint)asset.Manifest.IndexCount;
        _array = gl.GenVertexArray();
        _vertices = gl.GenBuffer();
        _indices = gl.GenBuffer();
        _particles = gl.GenBuffer();
        gl.BindVertexArray(_array);

        // The positions, then the colours, then the texture coordinates, in one buffer.
        float[] data = [.. asset.Positions, .. asset.Colors, .. asset.Uvs];
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vertices);
        fixed (float* pointer = data)
            gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(data.Length * sizeof(float)), pointer, BufferUsageARB.StaticDraw);
        gl.EnableVertexAttribArray(PositionAttribute);
        gl.VertexAttribPointer(PositionAttribute, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), (void*)0);
        gl.EnableVertexAttribArray(ColorAttribute);
        gl.VertexAttribPointer(ColorAttribute, 4, VertexAttribPointerType.Float, false, 4 * sizeof(float), (void*)(asset.Positions.Length * sizeof(float)));

        gl.EnableVertexAttribArray(TexCoordAttribute);
        gl.VertexAttribPointer(TexCoordAttribute, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float),
            (void*)((asset.Positions.Length + asset.Colors.Length) * sizeof(float)));

        gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _indices);
        fixed (uint* pointer = asset.Indices)
            gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(asset.Indices.Length * sizeof(uint)), pointer, BufferUsageARB.StaticDraw);

        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _particles);
        uint stride = ParticleFloats * sizeof(float);
        uint[] attributes = [LocalPositionAttribute, LocalVectorAttribute, ScaleAttribute, RandomAttribute, InitialRotationAttribute];
        for (int slot = 0; slot < attributes.Length; slot++)
        {
            gl.EnableVertexAttribArray(attributes[slot]);
            gl.VertexAttribPointer(attributes[slot], 4, VertexAttribPointerType.Float, false, stride, (void*)(slot * 4 * sizeof(float)));
            gl.VertexAttribDivisor(attributes[slot], 1);
        }
        gl.BindVertexArray(0);
    }

    public int Particles { get; private set; }

    public unsafe void SetParticles(float[] data)
    {
        Particles = data.Length / ParticleFloats;
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _particles);
        fixed (float* pointer = data)
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(data.Length * sizeof(float)), pointer, BufferUsageARB.DynamicDraw);
    }

    // The emitter's matrix as three rows, each the rotation row and then the position, which the programs apply to every particle's local position.
    public unsafe void Draw(Vector3 emitterPosition)
    {
        _gl.VertexAttrib4(EmitterMatrixAttribute, 1f, 0f, 0f, emitterPosition.X);
        _gl.VertexAttrib4(EmitterMatrixAttribute + 1, 0f, 1f, 0f, emitterPosition.Y);
        _gl.VertexAttrib4(EmitterMatrixAttribute + 2, 0f, 0f, 1f, emitterPosition.Z);
        _gl.BindVertexArray(_array);
        _gl.DrawElementsInstanced(_primitive, _indexCount, DrawElementsType.UnsignedInt, (void*)0, (uint)Particles);
        _gl.BindVertexArray(0);
    }

    public void Dispose()
    {
        _gl.DeleteVertexArray(_array);
        foreach (uint buffer in new[] { _vertices, _indices, _particles })
            _gl.DeleteBuffer(buffer);
    }
}
