using WildRenderingSharp.Assets.Materials;
using Silk.NET.OpenGL;
using WildRenderingSharp.Graphics.Contracts;
using WildRenderingSharp.Graphics.Ubos;

namespace WildRenderingSharp.Pipeline.Resources;

/// <summary>Persistent GL objects reused every frame: uniform buffers kept under a key, zeroed buffers by size, and the attribute-less VAO the fullscreen passes draw through.</summary>
internal sealed class GLResourceCache : IDisposable
{
    readonly GL _gl;
    readonly Dictionary<string, UniformBuffer> _ubos = new(StringComparer.Ordinal);
    readonly Dictionary<int, uint> _zeroed = [];
    readonly uint _attributelessVao;

    public GLResourceCache(GL gl, ShaderBindings bindings)
    {
        _gl = gl;
        Bindings = bindings;
        _attributelessVao = gl.GenVertexArray();
    }

    public GL Gl => _gl;

    public ShaderBindings Bindings { get; }

    // Writes the block into its buffer without binding it, to be bound later by BindUbo.
    public void Upload(Ubo ubo) => Write(ubo.Key, ubo.Bytes.Span);

    // Writes the block into its buffer and binds it at the binding its shader reads.
    public void Bind(Ubo ubo) => BindBase(ubo.Spec.Binding, Write(ubo.Key, ubo.Bytes.Span));

    public void Bind(IEnumerable<Ubo> blocks)
    {
        foreach (var ubo in blocks)
            Bind(ubo);
    }

    public void BindZeroed(UboSpec spec)
    {
        if (!_zeroed.TryGetValue(spec.ByteSize, out uint handle))
            _zeroed[spec.ByteSize] = handle = Write($"zero:{spec.ByteSize}", new byte[spec.ByteSize]);
        BindBase(spec.Binding, handle);
    }

    // Binds a block uploaded earlier; nothing happens when there is none under that key.
    public void BindUbo(string key, uint binding)
    {
        if (_ubos.TryGetValue(key, out var buffer))
            BindBase(binding, buffer.Handle);
    }

    public void BindCamera(string key) => BindUbo(key, Bindings.Camera);

    public void BindEnvironment() => BindUbo(FrameUniformKeys.Environment, Bindings.Environment);

    public void BindMaterial(MaterialBlock block) => BindBase(Bindings.Material, block.Handle);

    // The bytes last uploaded under the key, read back from the GPU for a debug view.
    public unsafe byte[] ReadUbo(string key)
    {
        if (!_ubos.TryGetValue(key, out var buffer))
            return [];
        _gl.BindBuffer(BufferTargetARB.UniformBuffer, buffer.Handle);
        _gl.GetBufferParameter(BufferTargetARB.UniformBuffer, BufferPNameARB.Size, out int size);
        byte[] bytes = new byte[size];
        fixed (byte* p = bytes)
            _gl.GetBufferSubData(BufferTargetARB.UniformBuffer, 0, (nuint)size, p);
        return bytes;
    }

    public void DrawFullscreenTriangle()
    {
        _gl.BindVertexArray(_attributelessVao);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
    }

    public void DrawFullscreenQuadStrip()
    {
        _gl.BindVertexArray(_attributelessVao);
        _gl.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
    }

    public void Dispose()
    {
        foreach (var buffer in _ubos.Values)
            _gl.DeleteBuffer(buffer.Handle);
        _ubos.Clear();
        _zeroed.Clear();
        _gl.DeleteVertexArray(_attributelessVao);
    }

    // Uploads the bytes unless the buffer under the key already holds exactly them, which most blocks of a frame do.
    uint Write(string key, ReadOnlySpan<byte> bytes)
    {
        if (!_ubos.TryGetValue(key, out var buffer))
            _ubos[key] = buffer = new UniformBuffer(_gl.GenBuffer());
        if (buffer.Holds(bytes))
            return buffer.Handle;

        _gl.BindBuffer(BufferTargetARB.UniformBuffer, buffer.Handle);
        _gl.BufferData(BufferTargetARB.UniformBuffer, bytes, BufferUsageARB.DynamicDraw);
        buffer.Remember(bytes);
        return buffer.Handle;
    }

    void BindBase(uint binding, uint buffer) => _gl.BindBufferBase(BufferTargetARB.UniformBuffer, binding, buffer);

    // A GL uniform buffer and a copy of what it holds, so an upload of the same bytes can be skipped.
    sealed class UniformBuffer(uint handle)
    {
        byte[] _contents = [];

        public uint Handle { get; } = handle;

        public bool Holds(ReadOnlySpan<byte> bytes) => bytes.SequenceEqual(_contents);

        public void Remember(ReadOnlySpan<byte> bytes)
        {
            if (_contents.Length != bytes.Length)
                _contents = new byte[bytes.Length];
            bytes.CopyTo(_contents);
        }
    }
}
