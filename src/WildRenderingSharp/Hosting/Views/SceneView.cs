using System.Numerics;
using Silk.NET.OpenGL;
using WildRenderingSharp.Gpu;
using WildRenderingSharp.Graphics.Contracts;
using WildRenderingSharp.Pipeline;
using WildRenderingSharp.Pipeline.Shadows;
using WildRenderingSharp.Pipeline.Targets;
using WildRenderingSharp.Rendering.Lighting;

namespace WildRenderingSharp.Hosting.Views;

/// <summary>
/// An offscreen view of a scene: renders a <see cref="FrameRequest"/> through a <see cref="DeferredPipeline"/> into an RGBA8 texture the
/// host displays. Every method needs the GL context current.
/// </summary>
public sealed class SceneView : IDisposable
{
    readonly GL _gl;
    readonly ScenePresenter _presenter;
    readonly ViewOutput _output;
    readonly RenderTargets? _ownTargets;
    readonly ShadowCache? _ownShadowCache;

    PresentGrade? _lastGrade;
    BackgroundMode _lastBackground;

    public SceneView(GL gl, DeferredPipeline pipeline, bool ownTargets = false)
    {
        _gl = gl;
        Pipeline = pipeline;
        _presenter = new ScenePresenter(gl);
        _output = new ViewOutput(gl);
        if (ownTargets)
        {
            _ownTargets = new RenderTargets(gl, 8, 8);
            _ownShadowCache = new ShadowCache();
        }
    }

    public DeferredPipeline Pipeline { get; }

    public AntiAliasingMode AntiAliasing { get; set; } = AntiAliasingMode.Supersample2x;

    public SceneViewMode Mode { get; set; } = SceneViewMode.Final;

    // For ResolvePass: which of the pipeline's deferred passes.
    public int ResolvePassIndex { get; set; }

    // Keeps the chosen resolve pass's output every frame, for ProbeCentre.
    public bool ProbeEnabled { get; set; }

    public int Supersample => AntiAliasing is AntiAliasingMode.Supersample2x or AntiAliasingMode.Supersample2xFxaa ? 2 : 1;

    public uint OutputTexture => _output.Texture;

    public uint OutputFramebuffer => _output.Framebuffer;

    public int Width => _output.Width;

    public int Height => _output.Height;

    public RenderTargets Targets => _ownTargets ?? Pipeline.Targets;

    public FrameResult? LastFrame { get; private set; }

    public uint DepthTexture => Targets.GBufferDepth.Handle;

    public bool OutputIsTopDown => Mode.IsTopDown();

    public (Vector2 Uv0, Vector2 Uv1) ImGuiUv => OutputIsTopDown
        ? (new Vector2(0, 0), new Vector2(1, 1))
        : (new Vector2(0, 1), new Vector2(1, 0));

    bool ApplyFxaa => AntiAliasing is AntiAliasingMode.Fxaa or AntiAliasingMode.Supersample2xFxaa;

    // The exact values of every input the deferred resolve reads, and what it wrote, at the middle of the frame. Set ProbeEnabled and
    // ResolvePassIndex first.
    public string ProbeCentre()
    {
        if (LastFrame is null)
            return "";

        // The readbacks bind a framebuffer of their own, and the host draws on with whatever is bound when this returns.
        Span<int> draw = stackalloc int[1], read = stackalloc int[1], viewport = stackalloc int[4];
        _gl.GetInteger(GetPName.DrawFramebufferBinding, draw);
        _gl.GetInteger(GetPName.ReadFramebufferBinding, read);
        _gl.GetInteger(GetPName.Viewport, viewport);
        try
        {
            string text = FrameProbe.Describe(_gl, Pipeline, Targets);
            DrawProbeMarker();
            return text;
        }
        finally
        {
            _gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, (uint)draw[0]);
            _gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, (uint)read[0]);
            _gl.Viewport(viewport[0], viewport[1], (uint)viewport[2], (uint)viewport[3]);
        }
    }

    // A box round the place the probe chose, in the view's own picture, so it can be seen where that is.
    void DrawProbeMarker()
    {
        if (FrameProbe.Marker is not var (x, y, width, height))
            return;
        int px = (int)((long)x * Width / width), py = Height - 1 - (int)((long)y * Height / height);
        const int Reach = 14, Thick = 2;
        bool scissor = _gl.IsEnabled(EnableCap.ScissorTest);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _output.Framebuffer);
        _gl.Enable(EnableCap.ScissorTest);
        _gl.ClearColor(1f, 0f, 1f, 1f);
        void Bar(int left, int bottom, int barWidth, int barHeight)
        {
            _gl.Scissor(Math.Max(left, 0), Math.Max(bottom, 0), (uint)Math.Max(barWidth, 0), (uint)Math.Max(barHeight, 0));
            _gl.Clear(ClearBufferMask.ColorBufferBit);
        }
        Bar(px - Reach, py - Reach, 2 * Reach + 1, Thick);
        Bar(px - Reach, py + Reach - Thick + 1, 2 * Reach + 1, Thick);
        Bar(px - Reach, py - Reach, Thick, 2 * Reach + 1);
        Bar(px + Reach - Thick + 1, py - Reach, Thick, 2 * Reach + 1);
        if (!scissor)
            _gl.Disable(EnableCap.ScissorTest);
    }

    public Vector4? ProbeHdr(Vector2 uvTopLeft)
    {
        if (LastFrame is not { } frame)
            return null;
        int px = Math.Clamp((int)(uvTopLeft.X * frame.Final.Width), 0, frame.Final.Width - 1);
        int py = Math.Clamp(frame.Final.Height - 1 - (int)(uvTopLeft.Y * frame.Final.Height), 0, frame.Final.Height - 1);
        return Targets.ReadPixel(frame.Final, px, py);
    }

    public FrameResult Render(FrameRequest request, int width, int height, GpuTexture? shadowMapOverride = null)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        _output.Ensure(width, height);
        Targets.Resize(width * Supersample, height * Supersample);

        if (Pipeline.Debug is { } debug)
            debug.DebugResolvePass = Mode == SceneViewMode.ResolvePass || ProbeEnabled ? ResolvePassIndex : -1;
        Pipeline.SnapshotStages = Mode.NeedsStageSnapshots();
        var frame = Pipeline.RenderFrame(request, _ownTargets, _ownShadowCache, shadowMapOverride);
        LastFrame = frame;
        _lastGrade = request.Environment.PresentGrade;
        _lastBackground = request.Lighting.Background;
        Present();
        Pipeline.Timer.EndFrame("present");
        return frame;
    }

    public void Present()
    {
        if (LastFrame is not { } frame || _lastGrade is not { } grade)
            return;
        _presenter.Present(Pipeline.Resources, Targets, frame, grade, _lastBackground, Mode, Supersample, ApplyFxaa, _output);
    }

    public byte[] ReadOutputRgba8() => _output.ReadRgba8(OutputIsTopDown);

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
        var restore = (_output.Width, _output.Height, LastFrame, _lastGrade, _lastBackground);
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
        var restore = (_output.Width, _output.Height, LastFrame, _lastGrade, _lastBackground);
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

    public void ReleaseTargets()
    {
        Targets.Resize(8, 8);
        _output.Ensure(8, 8);
        LastFrame = null;
    }

    public void Dispose()
    {
        _presenter.Dispose();
        _output.Dispose();
        _ownTargets?.Dispose();
    }

    void RestoreAfterOffscreen((int Width, int Height, FrameResult? Frame, PresentGrade? Grade, BackgroundMode Background) state)
    {
        if (state.Width > 0 && state.Height > 0)
        {
            _output.Ensure(state.Width, state.Height);
            Targets.Resize(state.Width * Supersample, state.Height * Supersample);
        }
        // The targets were reallocated, so the old frame's textures are gone and the caller must render again.
        LastFrame = null;
        _lastGrade = state.Grade;
        _lastBackground = state.Background;
    }
}
