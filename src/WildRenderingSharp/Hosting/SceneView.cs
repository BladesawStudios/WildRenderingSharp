using WildRenderingSharp.Graphics;
using System.Numerics;
using Silk.NET.OpenGL;
using WildRenderingSharp.Pipeline;
using WildRenderingSharp.Rendering;

namespace WildRenderingSharp.Hosting;

/// <summary>
/// An offscreen view of a scene: renders a <see cref="FrameRequest"/> through a <see cref="DeferredPipeline"/> and composites the
/// result (supersample downfilter, the palette's colour correction, sRGB encode, FXAA) into an RGBA8 texture the host displays
/// however it likes.
/// </summary>
public sealed class SceneView : IDisposable
{
    readonly GL _gl;
    readonly DeferredPipeline _pipeline;
    readonly PresentPass _present;
    readonly FxaaPass _fxaa;
    readonly RenderTargets? _ownTargets;
    readonly ShadowCache? _ownShadowCache;

    uint _outputFbo, _outputTexture;
    uint _gradedFbo, _gradedTexture;
    int _width, _height, _gradedWidth, _gradedHeight;

    PresentGrade? _lastGrade;
    BackgroundMode _lastBackground;

    public SceneView(GL gl, DeferredPipeline pipeline, bool ownTargets = false)
    {
        _gl = gl;
        _pipeline = pipeline;
        _present = new PresentPass(gl);
        _fxaa = new FxaaPass(gl);
        _outputFbo = gl.GenFramebuffer();
        _gradedFbo = gl.GenFramebuffer();
        if (ownTargets)
        {
            _ownTargets = new RenderTargets(gl, 8, 8);
            _ownShadowCache = new ShadowCache();
        }
    }

    public DeferredPipeline Pipeline => _pipeline;

    public AntiAliasingMode AntiAliasing { get; set; } = AntiAliasingMode.Supersample2x;

    public SceneViewMode Mode { get; set; } = SceneViewMode.Final;

    public int Supersample => AntiAliasing is AntiAliasingMode.Supersample2x or AntiAliasingMode.Supersample2xFxaa ? 2 : 1;

    bool ApplyFxaa => AntiAliasing is AntiAliasingMode.Fxaa or AntiAliasingMode.Supersample2xFxaa;

    public uint OutputTexture => _outputTexture;

    public uint OutputFramebuffer => _outputFbo;

    public int Width => _width;
    public int Height => _height;

    public RenderTargets Targets => _ownTargets ?? _pipeline.Targets;

    public FrameResult? LastFrame { get; private set; }

    public uint DepthTexture => Targets.GBufferDepth.Handle;

    public bool OutputIsTopDown => Mode is SceneViewMode.Albedo or SceneViewMode.Normal or SceneViewMode.Shadow or SceneViewMode.AmbientOcclusion;

    public (Vector2 Uv0, Vector2 Uv1) ImGuiUv => OutputIsTopDown
        ? (new Vector2(0, 0), new Vector2(1, 1))
        : (new Vector2(0, 1), new Vector2(1, 0));

    public FrameResult Render(FrameRequest request, int width, int height, GpuTexture? shadowMapOverride = null)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        EnsureOutput(width, height);

        int ss = Supersample;
        RenderTargets targets = Targets;
        targets.Resize(width * ss, height * ss);

        var frame = _pipeline.RenderFrame(request, _ownTargets, _ownShadowCache, shadowMapOverride);
        LastFrame = frame;
        _lastGrade = request.Environment.PresentGrade;
        _lastBackground = request.Lighting.Background;
        Present();
        _pipeline.Timer.EndFrame("present");
        return frame;
    }

    public void Present()
    {
        if (LastFrame is not { } frame || _lastGrade is not { } grade)
            return;

        int ss = Supersample;
        bool graded = Mode is SceneViewMode.Final or SceneViewMode.HdrPreview;
        var resources = _pipeline.Resources;

        // Graded views render into a native-resolution intermediate first so FXAA (or a passthrough) runs on the finished image; the raw diagnostic views skip grading and AA.
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, graded ? _gradedFbo : _outputFbo);
        _gl.Viewport(0, 0, (uint)_width, (uint)_height);
        _gl.Disable(EnableCap.Blend);
        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.ScissorTest);
        switch (Mode)
        {
            case SceneViewMode.HdrPreview:
                _present.Run(resources, frame.Final, ss, grade.Saturation, grade.Brightness, grade.Gamma, hdrPreview: true);
                break;
            case SceneViewMode.Albedo:
                _present.RunRaw(resources, frame.Albedo, 1f);
                break;
            case SceneViewMode.Normal:
                _present.RunRaw(resources, frame.Normal, 1f);
                break;
            case SceneViewMode.Shadow:
                _present.RunRaw(resources, frame.PreShadow, 1f);
                break;
            case SceneViewMode.AmbientOcclusion:
                _present.RunRaw(resources, frame.PreMisc, 1f);
                break;
            case SceneViewMode.PassId:
                // Values are i/255 for a handful of i, scaled up so distinct passes are visible.
                _present.RunRaw(resources, frame.PassId, 40f);
                break;
            default:
                // Coverage alpha for a transparent background comes from frame.Final, not frame.Ldr (see PresentPass.Run).
                _present.Run(resources, frame.Ldr, ss, grade.Saturation, grade.Brightness, grade.Gamma,
                    alphaSource: _lastBackground == BackgroundMode.Transparent ? frame.Final : null);
                break;
        }

        if (graded)
        {
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _outputFbo);
            _gl.Viewport(0, 0, (uint)_width, (uint)_height);
            var gradedTexture = new GpuTexture(_gradedTexture, _width, _height);
            if (ApplyFxaa)
                _fxaa.Run(resources, gradedTexture);
            else
                // alphaSource makes this an alpha passthrough rather than RunRaw's default of opaque.
                _present.RunRaw(resources, gradedTexture, 1f, alphaSource: gradedTexture);
        }
    }

    public Vector4? ProbeHdr(Vector2 uvTopLeft)
    {
        if (LastFrame is not { } frame)
            return null;
        int px = Math.Clamp((int)(uvTopLeft.X * frame.Final.Width), 0, frame.Final.Width - 1);
        int py = Math.Clamp(frame.Final.Height - 1 - (int)(uvTopLeft.Y * frame.Final.Height), 0, frame.Final.Height - 1);
        return Targets.ReadPixel(frame.Final, px, py);
    }

    public unsafe byte[] ReadOutputRgba8()
    {
        var raw = new byte[_width * _height * 4];
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _outputFbo);
        _gl.PixelStore(PixelStoreParameter.PackAlignment, 4);
        fixed (byte* p = raw)
            _gl.ReadPixels(0, 0, (uint)_width, (uint)_height, PixelFormat.Rgba, PixelType.UnsignedByte, p);
        if (OutputIsTopDown)
            return raw;

        var flipped = new byte[raw.Length];
        int stride = _width * 4;
        for (int y = 0; y < _height; y++)
            Array.Copy(raw, y * stride, flipped, (_height - 1 - y) * stride, stride);
        return flipped;
    }

    public float[]? ReadHdrRgba(out int width, out int height)
    {
        width = height = 0;
        if (LastFrame is not { } frame)
            return null;
        width = frame.Final.Width;
        height = frame.Final.Height;
        return Targets.ReadPixelsFloatRgba(frame.Final);
    }

    public byte[] RenderToRgba8(FrameRequest request, int width, int height)
    {
        var restore = (_width, _height, LastFrame, _lastGrade, _lastBackground);
        try
        {
            Render(request, width, height);
            return ReadOutputRgba8();
        }
        finally
        {
            RestoreAfterOffscreen(restore);
        }
    }

    public float[] RenderToHdr(FrameRequest request, int width, int height)
    {
        var restore = (_width, _height, LastFrame, _lastGrade, _lastBackground);
        var aa = AntiAliasing;
        try
        {
            AntiAliasing = AntiAliasingMode.Off;
            Render(request, width, height);
            return ReadHdrRgba(out _, out _)!;
        }
        finally
        {
            AntiAliasing = aa;
            RestoreAfterOffscreen(restore);
        }
    }

    void RestoreAfterOffscreen((int Width, int Height, FrameResult? Frame, PresentGrade? Grade, BackgroundMode Background) state)
    {
        if (state.Width > 0 && state.Height > 0)
        {
            EnsureOutput(state.Width, state.Height);
            Targets.Resize(state.Width * Supersample, state.Height * Supersample);
        }
        // The targets were reallocated, so the old frame's textures are gone and the caller must render again.
        LastFrame = null;
        _lastGrade = state.Grade;
        _lastBackground = state.Background;
    }

    unsafe void EnsureOutput(int width, int height)
    {
        if (width != _width || height != _height || _outputTexture == 0)
        {
            if (_outputTexture != 0)
                _gl.DeleteTexture(_outputTexture);
            _outputTexture = CreateColorTexture(width, height, _outputFbo);
            _width = width;
            _height = height;
        }
        if (width != _gradedWidth || height != _gradedHeight || _gradedTexture == 0)
        {
            if (_gradedTexture != 0)
                _gl.DeleteTexture(_gradedTexture);
            _gradedTexture = CreateColorTexture(width, height, _gradedFbo);
            _gradedWidth = width;
            _gradedHeight = height;
        }
    }

    unsafe uint CreateColorTexture(int width, int height, uint fbo)
    {
        uint texture = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, texture);
        _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)width, (uint)height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, null);
        _gl.SetSampling(TextureTarget.Texture2D, GLEnum.Linear, GLEnum.ClampToEdge);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, fbo);
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, texture, 0);
        return texture;
    }

    public void ReleaseTargets()
    {
        Targets.Resize(8, 8);
        EnsureOutput(8, 8);
        LastFrame = null;
    }

    public void Dispose()
    {
        _present.Dispose();
        _fxaa.Dispose();
        _ownTargets?.Dispose();
        if (_outputTexture != 0) _gl.DeleteTexture(_outputTexture);
        if (_gradedTexture != 0) _gl.DeleteTexture(_gradedTexture);
        _gl.DeleteFramebuffer(_outputFbo);
        _gl.DeleteFramebuffer(_gradedFbo);
    }
}
