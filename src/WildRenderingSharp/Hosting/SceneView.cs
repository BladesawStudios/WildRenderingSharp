using System.Numerics;
using Silk.NET.OpenGL;
using WildRenderingSharp.Pipeline;
using WildRenderingSharp.Rendering;

namespace WildRenderingSharp.Hosting;

/// <summary>Anti-aliasing for a <see cref="SceneView"/>.</summary>
public enum AntiAliasingMode
{
    Off,
    Fxaa,
    /// <summary>Renders at twice the output size in each dimension and box-filters down in linear light.</summary>
    Supersample2x,
    Supersample2xFxaa,
}

/// <summary>What a <see cref="SceneView"/> shows.</summary>
public enum SceneViewMode
{
    /// <summary>The graded, tonemapped frame.</summary>
    Final,
    /// <summary>The raw HDR buffer before exposure, Reinhard-mapped - for judging what the shaders output.</summary>
    HdrPreview,
    /// <summary>G-buffer albedo.</summary>
    Albedo,
    /// <summary>G-buffer normal (packed .xy only - "is the G-buffer populated at all").</summary>
    Normal,
    /// <summary><c>cTex_PreShadow</c>, the renderer's synthesised sun visibility.</summary>
    Shadow,
    /// <summary><c>cTex_PreMisc</c>, the renderer's synthesised screen-space AO / N.L.</summary>
    AmbientOcclusion,
    /// <summary>The deferred-resolve pass mask, scaled so distinct passes show as distinct greys.</summary>
    PassId,
}

/// <summary>
/// An offscreen view of a scene: renders a <see cref="FrameRequest"/> through a
/// <see cref="DeferredPipeline"/> and composites the result - supersample downfilter, the palette's
/// colour correction, sRGB encode, FXAA - into an ordinary RGBA8 texture the host displays however
/// it likes (an ImGui image, a blit into its own framebuffer, a readback to a file).
/// </summary>
/// <remarks>
/// <para>
/// Several views can share one pipeline - a main viewport plus a picture-in-picture camera
/// preview, say. The main one renders through the pipeline's own <see cref="RenderTargets"/>; a
/// secondary one is created with <c>ownTargets: true</c> and gets its own targets and its own
/// <see cref="ShadowCache"/>. That is not an optimisation detail: resizing shared targets back and
/// forth every frame reallocated every G-buffer texture twice a frame, and two views sharing one
/// shadow cache invalidate each other on every frame.
/// </para>
/// <para>
/// <see cref="OutputTexture"/> is stored the GL way, bottom row first - except in the four raw
/// G-buffer-space modes (Albedo/Normal/Shadow/AO), which the pipeline rasterises through a
/// Y-flipped projection and so come out top row first. <see cref="OutputIsTopDown"/> says which,
/// and <see cref="ImGuiUv"/> gives the matching texture coordinates for an <c>ImGui.Image</c>.
/// </para>
/// <para>Every method needs the GL context current. None of them change the host's GL state
/// conventions; wrap calls in <see cref="GLHostState"/> if the host has changed GL's defaults.</para>
/// </remarks>
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

    EnvPalette? _lastPalette;
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

    /// <summary>
    /// 2x supersampling by default. It was once pulled in favour of FXAA-only after it produced
    /// evenly spaced black bands on thin bright geometry; the real cause turned out to be the
    /// bit-packed G-buffer attachments defaulting to LINEAR filtering (fixed in
    /// <see cref="RenderTargets"/>), not supersampling itself.
    /// </summary>
    public AntiAliasingMode AntiAliasing { get; set; } = AntiAliasingMode.Supersample2x;

    public SceneViewMode Mode { get; set; } = SceneViewMode.Final;

    /// <summary>Render-target scale factor <see cref="AntiAliasing"/> implies.</summary>
    public int Supersample => AntiAliasing is AntiAliasingMode.Supersample2x or AntiAliasingMode.Supersample2xFxaa ? 2 : 1;

    bool ApplyFxaa => AntiAliasing is AntiAliasingMode.Fxaa or AntiAliasingMode.Supersample2xFxaa;

    /// <summary>The composited RGBA8 result. Zero until the first <see cref="Render"/>.</summary>
    public uint OutputTexture => _outputTexture;

    /// <summary>A framebuffer with <see cref="OutputTexture"/> attached - handy as a blit source.</summary>
    public uint OutputFramebuffer => _outputFbo;

    public int Width => _width;
    public int Height => _height;

    /// <summary>The targets this view renders through (its own, or the pipeline's).</summary>
    public RenderTargets Targets => _ownTargets ?? _pipeline.Targets;

    /// <summary>The last frame rendered, for re-presenting or probing without re-rendering.</summary>
    public FrameResult? LastFrame { get; private set; }

    /// <summary>
    /// The last frame's depth, for a host compositing it into a scene of its own: standard GL
    /// [0, 1] depth through the request camera's own projection, cleared to 1 where nothing was
    /// drawn, at the render size (<see cref="Supersample"/> times the output) - and stored the
    /// G-buffer's way up, i.e. upside down relative to <see cref="OutputTexture"/>.
    /// </summary>
    public uint DepthTexture => Targets.GBufferDepth.Handle;

    /// <summary>True when <see cref="OutputTexture"/> holds its top row first (the raw G-buffer-space modes) - see the class remarks.</summary>
    public bool OutputIsTopDown => Mode is SceneViewMode.Albedo or SceneViewMode.Normal or SceneViewMode.Shadow or SceneViewMode.AmbientOcclusion;

    /// <summary>The <c>uv0</c>/<c>uv1</c> pair that shows <see cref="OutputTexture"/> upright in a top-left-origin UI such as ImGui.</summary>
    public (Vector2 Uv0, Vector2 Uv1) ImGuiUv => OutputIsTopDown
        ? (new Vector2(0, 0), new Vector2(1, 1))
        : (new Vector2(0, 1), new Vector2(1, 0));

    /// <summary>
    /// Renders one frame at <paramref name="width"/>x<paramref name="height"/> (output pixels; the
    /// render itself is <see cref="Supersample"/> times that) and composites it into
    /// <see cref="OutputTexture"/>.
    /// </summary>
    /// <param name="shadowMapOverride">Reuse another view's shadow map instead of drawing one - the shadow map depends on the scene and the sun, never the camera, so a secondary view of the same scene can take the main view's.</param>
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
        _lastPalette = request.Palette;
        _lastBackground = request.Lighting.Background;
        Present();
        _pipeline.Timer.EndFrame("present");
        return frame;
    }

    /// <summary>
    /// Re-composites <see cref="LastFrame"/> into <see cref="OutputTexture"/> - after a change of
    /// <see cref="Mode"/> or FXAA, which do not need the scene rendered again. A change of
    /// supersampling does, since it changes the render size.
    /// </summary>
    public void Present()
    {
        if (LastFrame is not { } frame || _lastPalette is not { } pal)
            return;

        int ss = Supersample;
        bool graded = Mode is SceneViewMode.Final or SceneViewMode.HdrPreview;
        float saturation = pal.ColorCorrectEnable ? pal.ColorCorrectSaturation : 1f;
        float brightness = pal.ColorCorrectEnable ? pal.ColorCorrectBrightness : 1f;
        float gamma = pal.ColorCorrectEnable ? pal.ColorCorrectGamma : 1f;
        var resources = _pipeline.Resources;

        // The graded views render into the native-resolution intermediate first, so FXAA (or a
        // plain passthrough when it is off) can run on the finished image without re-deriving
        // it; the raw diagnostic views skip grading and AA entirely.
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, graded ? _gradedFbo : _outputFbo);
        _gl.Viewport(0, 0, (uint)_width, (uint)_height);
        _gl.Disable(EnableCap.Blend);
        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.ScissorTest);
        switch (Mode)
        {
            case SceneViewMode.HdrPreview:
                _present.Run(resources, frame.Final, ss, saturation, brightness, gamma, hdrPreview: true);
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
                // Real per-pixel coverage alpha (Background: Transparent) comes from frame.Final,
                // not frame.Ldr itself - see PresentPass.Run's own remarks on why.
                _present.Run(resources, frame.Ldr, ss, saturation, brightness, gamma,
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
                // alphaSource: itself makes this a genuine alpha PASSTHROUGH rather than RunRaw's
                // usual diagnostic "hardcode opaque" default.
                _present.RunRaw(resources, gradedTexture, 1f, alphaSource: gradedTexture);
        }
    }

    /// <summary>
    /// The exact HDR value under a point of the view, BEFORE exposure/tonemap - "what is this
    /// shader value really doing here", which a graded image cannot answer.
    /// </summary>
    /// <param name="uvTopLeft">0..1 within the displayed image, top-left origin.</param>
    public Vector4? ProbeHdr(Vector2 uvTopLeft)
    {
        if (LastFrame is not { } frame)
            return null;
        int px = Math.Clamp((int)(uvTopLeft.X * frame.Final.Width), 0, frame.Final.Width - 1);
        int py = Math.Clamp(frame.Final.Height - 1 - (int)(uvTopLeft.Y * frame.Final.Height), 0, frame.Final.Height - 1);
        return Targets.ReadPixel(frame.Final, px, py);
    }

    /// <summary><see cref="OutputTexture"/> read back as top-down RGBA8 rows - exactly what the view shows.</summary>
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

    /// <summary>
    /// The last frame's scene-referred linear HDR buffer (BEFORE exposure/tonemap) as top-down
    /// RGBA float rows, at the render resolution - for a real <c>.hdr</c> export the viewer's own
    /// exposure is not baked into.
    /// </summary>
    public float[]? ReadHdrRgba(out int width, out int height)
    {
        width = height = 0;
        if (LastFrame is not { } frame)
            return null;
        width = frame.Final.Width;
        height = frame.Final.Height;
        return Targets.ReadPixelsFloatRgba(frame.Final);
    }

    /// <summary>
    /// Renders one frame at an arbitrary size and returns it as top-down RGBA8, leaving the view
    /// exactly as it was (its size, its last frame) afterwards - for an export at a resolution
    /// other than the on-screen one. Honours <see cref="AntiAliasing"/>, so an export is never
    /// lower quality than the view it was taken from.
    /// </summary>
    public byte[] RenderToRgba8(FrameRequest request, int width, int height)
    {
        var restore = (_width, _height, LastFrame, _lastPalette, _lastBackground);
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

    /// <summary>The HDR counterpart of <see cref="RenderToRgba8"/> - rendered without supersampling, so the result is exactly <paramref name="width"/>x<paramref name="height"/>.</summary>
    public float[] RenderToHdr(FrameRequest request, int width, int height)
    {
        var restore = (_width, _height, LastFrame, _lastPalette, _lastBackground);
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

    void RestoreAfterOffscreen((int Width, int Height, FrameResult? Frame, EnvPalette? Palette, BackgroundMode Background) state)
    {
        if (state.Width > 0 && state.Height > 0)
        {
            EnsureOutput(state.Width, state.Height);
            Targets.Resize(state.Width * Supersample, state.Height * Supersample);
        }
        // The targets were reallocated, so the old frame's textures are gone; the caller renders
        // again before presenting anything.
        LastFrame = null;
        _lastPalette = state.Palette;
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
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, fbo);
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, texture, 0);
        return texture;
    }

    /// <summary>
    /// Shrinks the render targets and outputs to almost nothing, returning their memory - the
    /// G-buffer, HDR and post targets at a large window and 2x supersampling run to well over a
    /// gigabyte. For a host that keeps the view while not showing it; the next <see cref="Render"/>
    /// grows them back.
    /// </summary>
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
