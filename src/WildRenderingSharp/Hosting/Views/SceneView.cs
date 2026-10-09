using System.Numerics;
using Silk.NET.OpenGL;
using WildRenderingSharp.Gpu;
using WildRenderingSharp.Graphics.Contracts;
using WildRenderingSharp.Pipeline;
using WildRenderingSharp.Pipeline.Shadows;
using WildRenderingSharp.Pipeline.Targets;
using WildRenderingSharp.Rendering.Lighting;

namespace WildRenderingSharp.Hosting.Views;

/// <summary>An offscreen view of a scene: renders a <see cref="FrameRequest"/> through a <see cref="DeferredPipeline"/> into an RGBA8 texture the host displays. Every method needs the GL context current.</summary>
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
    public string ProbeCentre() => LastFrame is null ? "" : FrameProbe.Describe(_gl, Pipeline, Targets);

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
