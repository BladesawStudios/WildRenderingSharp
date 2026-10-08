using Silk.NET.OpenGL;
using WildRenderingSharp.Graphics;

namespace WildRenderingSharp.Pipeline;

/// <summary>
/// Persistent GL objects that would otherwise be reallocated every frame: named uniform buffers (rewritten in place rather than
/// recreated) and the one attribute-less VAO every fullscreen pass draws through (the deferred vertex shaders synthesise position
/// and UV from <c>gl_VertexID</c>).
/// </summary>
public sealed class GLResourceCache : IDisposable
{
    readonly GL _gl;
    readonly Dictionary<string, uint> _ubos = new(StringComparer.Ordinal);
    readonly uint _attributelessVao;

    public ShaderBindings Bindings { get; }

    public GLResourceCache(GL gl, ShaderBindings bindings)
    {
        _gl = gl;
        Bindings = bindings;
        _attributelessVao = gl.GenVertexArray();
    }

    public uint Ubo(string key, ReadOnlySpan<byte> data, uint? bindingIndex = null)
    {
        if (!_ubos.TryGetValue(key, out uint handle))
        {
            handle = _gl.GenBuffer();
            _ubos[key] = handle;
        }
        _gl.BindBuffer(BufferTargetARB.UniformBuffer, handle);
        _gl.BufferData(BufferTargetARB.UniformBuffer, data, BufferUsageARB.DynamicDraw);
        if (bindingIndex is uint b)
            _gl.BindBufferBase(BufferTargetARB.UniformBuffer, b, handle);
        return handle;
    }

    public GL Gl => _gl;

    public void BindZeroUbo(uint bindingIndex, int size = 256)
    {
        if (!_zeroUbos.TryGetValue(size, out uint handle))
        {
            string key = $"zero:{size}";
            if (!_ubos.ContainsKey(key))
                Ubo(key, new byte[size]);
            handle = _ubos[key];
            _zeroUbos[size] = handle;
        }
        _gl.BindBufferBase(BufferTargetARB.UniformBuffer, bindingIndex, handle);
    }

    readonly Dictionary<int, uint> _zeroUbos = [];

    public void BindUbo(string key, uint bindingIndex)
    {
        if (_ubos.TryGetValue(key, out uint handle))
            _gl.BindBufferBase(BufferTargetARB.UniformBuffer, bindingIndex, handle);
    }

    public void BindCamera(string key) => BindUbo(key, Bindings.Camera);

    public void BindEnvironment() => BindUbo(FrameUniformKeys.Environment, Bindings.Environment);

    public void BindMaterial(uint buffer) => _gl.BindBufferBase(BufferTargetARB.UniformBuffer, Bindings.Material, buffer);

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
        foreach (uint h in _ubos.Values)
            _gl.DeleteBuffer(h);
        _ubos.Clear();
        _zeroUbos.Clear();
        _gl.DeleteVertexArray(_attributelessVao);
    }
}
