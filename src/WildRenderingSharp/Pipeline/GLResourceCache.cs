using Silk.NET.OpenGL;

namespace WildRenderingSharp.Pipeline;

/// <summary>
/// Persistent GL objects that would otherwise get reallocated every frame - named uniform
/// buffers (rewritten in place rather than recreated) and the one attribute-less VAO every
/// fullscreen pass draws through (the deferred vertex shaders synthesise their own position/UV
/// from <c>gl_VertexID</c>, so no vertex buffer is needed at all).
///
/// Mirrors <c>viewer.Viewer</c>'s <c>_ubo</c>/<c>_tri</c> helpers, which exist specifically
/// because allocating a fresh buffer/VAO inside the frame - which an early version of the bench
/// did - hands the driver a new live GL object every frame and only frees it whenever Python's GC
/// happens to run, so the process slows down and leaks over a session.
/// </summary>
public sealed class GLResourceCache : IDisposable
{
    readonly GL _gl;
    readonly Dictionary<string, uint> _ubos = new(StringComparer.Ordinal);
    readonly uint _attributelessVao;

    public GLResourceCache(GL gl)
    {
        _gl = gl;
        _attributelessVao = gl.GenVertexArray();
    }

    /// <summary>Rewrites (or creates) a named uniform buffer's contents, optionally binding it to a binding point immediately.</summary>
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

    /// <summary>Binds an already-written named buffer to a binding point without rewriting it - for rebinding <c>Context</c> between its several per-frame variants.</summary>
    /// <summary>The context these resources belong to.</summary>
    public GL Gl => _gl;

    /// <summary>Binds a block of <paramref name="size"/> zero bytes, uploaded once and kept.</summary>
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

    uint _windSwell, _lieMap, _thickness;

    /// <summary>
    /// Binds each engine-rendered vertex texture's neutral at its own unit (see
    /// <see cref="GlslSanitizer.WindSwellUnit"/>): no wind swell, grass pressed nowhere (the lie map
    /// is read as <c>2x - 1</c>, so 0.5 is no push), no thickness.
    /// </summary>
    public void BindEngineVertexTextures()
    {
        if (_windSwell == 0)
        {
            _windSwell = Constant(0f, 0f);
            _lieMap = Constant(0.5f, 0.5f);
            _thickness = Constant(0f, 0f);
        }
        Bind(GlslSanitizer.WindSwellUnit, _windSwell);
        Bind(GlslSanitizer.LieMapUnit, _lieMap);
        Bind(GlslSanitizer.ThicknessUnit, _thickness);
        _gl.ActiveTexture(TextureUnit.Texture0);

        void Bind(int unit, uint handle)
        {
            _gl.ActiveTexture(TextureUnit.Texture0 + unit);
            _gl.BindTexture(TextureTarget.Texture2D, handle);
        }
    }

    unsafe uint Constant(float r, float g)
    {
        uint handle = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, handle);
        float* texel = stackalloc float[] { r, g, 0f, 1f };
        _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba16f, 1, 1, 0, PixelFormat.Rgba, PixelType.Float, texel);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
        _gl.BindTexture(TextureTarget.Texture2D, 0);
        return handle;
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
        foreach (uint h in _ubos.Values)
            _gl.DeleteBuffer(h);
        _ubos.Clear();
        _zeroUbos.Clear();
        _gl.DeleteVertexArray(_attributelessVao);
        foreach (uint t in new[] { _windSwell, _lieMap, _thickness })
            if (t != 0)
                _gl.DeleteTexture(t);
    }
}
