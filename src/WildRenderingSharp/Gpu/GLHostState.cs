using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.ARB;

namespace WildRenderingSharp.Gpu;

/// <summary>
/// Puts the GL context into the default state the renderer was written against, and puts the host's own state back afterwards:
/// <c>using (GLHostState.Enter(gl)) { ... render ... }</c>.
/// </summary>
public sealed class GLHostState : IDisposable
{
    public const int SavedBufferBindings = 16;

    readonly GL _gl;
    readonly ArbClipControl? _clipControl;
    bool _disposed;

    // Saved host state.
    readonly int _clipOrigin, _clipDepth;
    readonly bool _clipKnown;
    readonly int _readFbo, _drawFbo, _program, _vao, _activeTexture, _arrayBuffer;
    readonly int[] _viewport = new int[4];
    readonly bool[] _colorMask = new bool[4];
    readonly bool _depthMask;
    readonly float _clearDepth;
    readonly float[] _clearColor = new float[4];
    readonly int _depthFunc, _cullMode, _frontFace, _blendSrcRgb, _blendDstRgb, _blendSrcAlpha, _blendDstAlpha, _blendEqRgb, _blendEqAlpha;
    readonly int _unpackAlignment, _unpackRowLength, _packAlignment;
    readonly float _polygonOffsetFactor, _polygonOffsetUnits;
    readonly int[] _polygonMode = new int[2];
    readonly (EnableCap Cap, bool On)[] _caps;
    readonly (int Buffer, long Start, long Size)[] _ubos = new (int, long, long)[SavedBufferBindings];
    readonly (int Buffer, long Start, long Size)[] _ssbos = new (int, long, long)[SavedBufferBindings];

    static readonly EnableCap[] TrackedCaps =
    [
        EnableCap.DepthTest, EnableCap.Blend, EnableCap.CullFace, EnableCap.ScissorTest, EnableCap.StencilTest,
        EnableCap.PolygonOffsetFill, EnableCap.PolygonOffsetLine, EnableCap.PolygonOffsetPoint,
        EnableCap.FramebufferSrgb, EnableCap.DepthClamp, EnableCap.ProgramPointSize, EnableCap.PrimitiveRestart,
        EnableCap.RasterizerDiscard, EnableCap.SampleAlphaToCoverage, EnableCap.Multisample,
    ];

    GLHostState(GL gl)
    {
        _gl = gl;
        _clipControl = TryClipControl(gl);

        if (_clipControl is not null)
        {
            // An error the host left pending would otherwise read as "clip control queries are unsupported" below.
            DrainErrors(gl);
            _clipOrigin = gl.GetInteger((GetPName)GLEnum.ClipOrigin);
            _clipDepth = gl.GetInteger((GetPName)GLEnum.ClipDepthMode);
            _clipKnown = gl.GetError() == GLEnum.NoError;
        }

        _readFbo = gl.GetInteger(GetPName.ReadFramebufferBinding);
        _drawFbo = gl.GetInteger(GetPName.DrawFramebufferBinding);
        _program = gl.GetInteger(GetPName.CurrentProgram);
        _vao = gl.GetInteger(GetPName.VertexArrayBinding);
        _activeTexture = gl.GetInteger(GetPName.ActiveTexture);
        _arrayBuffer = gl.GetInteger(GetPName.ArrayBufferBinding);
        gl.GetInteger(GetPName.Viewport, _viewport);

        Span<int> mask = stackalloc int[4];
        gl.GetInteger(GetPName.ColorWritemask, mask);
        for (int i = 0; i < 4; i++)
            _colorMask[i] = mask[i] != 0;
        _depthMask = gl.GetBoolean(GetPName.DepthWritemask);
        _clearDepth = gl.GetFloat(GetPName.DepthClearValue);
        gl.GetFloat(GetPName.ColorClearValue, _clearColor);

        _depthFunc = gl.GetInteger(GetPName.DepthFunc);
        _cullMode = gl.GetInteger(GetPName.CullFaceMode);
        _frontFace = gl.GetInteger(GetPName.FrontFace);
        _blendSrcRgb = gl.GetInteger(GetPName.BlendSrcRgb);
        _blendDstRgb = gl.GetInteger(GetPName.BlendDstRgb);
        _blendSrcAlpha = gl.GetInteger(GetPName.BlendSrcAlpha);
        _blendDstAlpha = gl.GetInteger(GetPName.BlendDstAlpha);
        _blendEqRgb = gl.GetInteger(GetPName.BlendEquationRgb);
        _blendEqAlpha = gl.GetInteger(GetPName.BlendEquationAlpha);
        _unpackAlignment = gl.GetInteger(GetPName.UnpackAlignment);
        _unpackRowLength = gl.GetInteger(GetPName.UnpackRowLength);
        _packAlignment = gl.GetInteger(GetPName.PackAlignment);
        _polygonOffsetFactor = gl.GetFloat(GetPName.PolygonOffsetFactor);
        _polygonOffsetUnits = gl.GetFloat(GetPName.PolygonOffsetUnits);
        gl.GetInteger(GetPName.PolygonMode, _polygonMode);

        _caps = new (EnableCap, bool)[TrackedCaps.Length];
        for (int i = 0; i < TrackedCaps.Length; i++)
            _caps[i] = (TrackedCaps[i], gl.IsEnabled(TrackedCaps[i]));

        for (uint i = 0; i < SavedBufferBindings; i++)
        {
            _ubos[i] = ReadIndexed(GLEnum.UniformBufferBinding, GLEnum.UniformBufferStart, GLEnum.UniformBufferSize, i);
            _ssbos[i] = ReadIndexed(GLEnum.ShaderStorageBufferBinding, GLEnum.ShaderStorageBufferStart, GLEnum.ShaderStorageBufferSize, i);
        }

        // Anything a query above raised (an enum this context does not know) is ours, not the renderer's; drain it so GLDiagnostics does not blame the first pass.
        DrainErrors(gl);

        ApplyDefaults();
    }

    public static GLHostState Enter(GL gl) => new(gl);

    (int, long, long) ReadIndexed(GLEnum binding, GLEnum start, GLEnum size, uint index)
    {
        _gl.GetInteger(binding, index, out int buffer);
        _gl.GetInteger64(start, index, out long s);
        _gl.GetInteger64(size, index, out long z);
        return (buffer, s, z);
    }

    static ArbClipControl? TryClipControl(GL gl)
    {
        try
        {
            return gl.TryGetExtension(out ArbClipControl ext) ? ext : null;
        }
        catch
        {
            return null;
        }
    }

    void ApplyDefaults()
    {
        var gl = _gl;
        if (_clipKnown && (_clipOrigin != (int)GLEnum.LowerLeft || _clipDepth != (int)GLEnum.NegativeOneToOne))
            _clipControl!.ClipControl((ARB)GLEnum.LowerLeft, (ARB)GLEnum.NegativeOneToOne);

        gl.ColorMask(true, true, true, true);
        gl.DepthMask(true);
        gl.ClearDepth(1.0);
        gl.DepthFunc(DepthFunction.Less);
        gl.CullFace(TriangleFace.Back);
        gl.FrontFace(FrontFaceDirection.Ccw);
        gl.BlendFunc(BlendingFactor.One, BlendingFactor.Zero);
        gl.BlendEquation(BlendEquationModeEXT.FuncAdd);
        gl.PolygonOffset(0f, 0f);
        gl.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Fill);
        gl.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
        gl.PixelStore(PixelStoreParameter.UnpackRowLength, 0);
        gl.PixelStore(PixelStoreParameter.PackAlignment, 4);
        foreach (var (cap, _) in _caps)
        {
            if (cap == EnableCap.Multisample)
                gl.Enable(cap); // GL's own default
            else
                gl.Disable(cap);
        }
        gl.ActiveTexture(TextureUnit.Texture0);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        var gl = _gl;

        if (_clipKnown)
            _clipControl!.ClipControl((ARB)_clipOrigin, (ARB)_clipDepth);

        gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, (uint)_readFbo);
        gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, (uint)_drawFbo);
        gl.Viewport(_viewport[0], _viewport[1], (uint)_viewport[2], (uint)_viewport[3]);
        gl.UseProgram((uint)_program);
        gl.BindVertexArray((uint)_vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, (uint)_arrayBuffer);
        gl.ActiveTexture((TextureUnit)_activeTexture);

        gl.ColorMask(_colorMask[0], _colorMask[1], _colorMask[2], _colorMask[3]);
        gl.DepthMask(_depthMask);
        gl.ClearDepth(_clearDepth);
        gl.ClearColor(_clearColor[0], _clearColor[1], _clearColor[2], _clearColor[3]);
        gl.DepthFunc((DepthFunction)_depthFunc);
        gl.CullFace((TriangleFace)_cullMode);
        gl.FrontFace((FrontFaceDirection)_frontFace);
        gl.BlendFuncSeparate((BlendingFactor)_blendSrcRgb, (BlendingFactor)_blendDstRgb, (BlendingFactor)_blendSrcAlpha, (BlendingFactor)_blendDstAlpha);
        gl.BlendEquationSeparate((BlendEquationModeEXT)_blendEqRgb, (BlendEquationModeEXT)_blendEqAlpha);
        gl.PixelStore(PixelStoreParameter.UnpackAlignment, _unpackAlignment);
        gl.PixelStore(PixelStoreParameter.UnpackRowLength, _unpackRowLength);
        gl.PixelStore(PixelStoreParameter.PackAlignment, _packAlignment);
        gl.PolygonOffset(_polygonOffsetFactor, _polygonOffsetUnits);
        gl.PolygonMode(TriangleFace.FrontAndBack, (PolygonMode)_polygonMode[0]);
        foreach (var (cap, on) in _caps)
        {
            if (on) gl.Enable(cap);
            else gl.Disable(cap);
        }

        for (uint i = 0; i < SavedBufferBindings; i++)
        {
            Restore(BufferTargetARB.UniformBuffer, i, _ubos[i]);
            Restore(BufferTargetARB.ShaderStorageBuffer, i, _ssbos[i]);
        }

        DrainErrors(gl);
    }

    static void DrainErrors(GL gl)
    {
        for (int i = 0; i < 32 && gl.GetError() != GLEnum.NoError; i++) { }
    }

    void Restore(BufferTargetARB target, uint index, (int Buffer, long Start, long Size) binding)
    {
        if (binding.Buffer != 0 && binding.Size > 0)
            _gl.BindBufferRange(target, index, (uint)binding.Buffer, (nint)binding.Start, (nuint)binding.Size);
        else
            _gl.BindBufferBase(target, index, (uint)binding.Buffer);
    }
}
