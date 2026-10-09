using Silk.NET.OpenGL;
using WildRenderingSharp.Graphics.Contracts;
using WildRenderingSharp.Pipeline;
using WildRenderingSharp.Pipeline.Gpu;
using WildRenderingSharp.Pipeline.Passes;
using WildRenderingSharp.Rendering.Lighting;

namespace WildRenderingSharp.Hosting.Views;

/// <summary>Draws a rendered frame into a view's output for the chosen mode: graded and anti-aliased for the beauty views, raw for the diagnostic ones.</summary>
sealed class ScenePresenter(GL gl) : IDisposable
{
    // PassId values are i/255 for a handful of i, scaled up so distinct passes are visible.
    const float PassIdScale = 40f, LastResolveScale = 0.1f;

    readonly PresentPass _present = new(gl);
    readonly FxaaPass _fxaa = new(gl);

    public void Present(GLResourceCache resources, RenderTargets targets, FrameResult frame, PresentGrade grade, BackgroundMode background,
        SceneViewMode mode, int supersample, bool fxaa, ViewOutput output)
    {
        bool graded = mode.IsGraded();
        // Graded views render into a native-resolution intermediate first so FXAA (or a passthrough) runs on the finished image.
        output.Bind(graded);

        if (RawView(mode, targets, frame) is var (texture, scale))
            _present.RunRaw(resources, texture, scale);
        else if (HdrView(mode, targets, frame) is { } hdr)
            _present.Run(resources, hdr, supersample, grade.Saturation, grade.Brightness, grade.Gamma, hdrPreview: true);
        else
            // Coverage alpha for a transparent background comes from frame.Final, not frame.Ldr (see PresentPass.Run).
            _present.Run(resources, frame.Ldr, supersample, grade.Saturation, grade.Brightness, grade.Gamma,
                alphaSource: background == BackgroundMode.Transparent ? frame.Final : null);

        if (graded)
            Finish(resources, output, fxaa);
    }

    public void Dispose()
    {
        _present.Dispose();
        _fxaa.Dispose();
    }

    void Finish(GLResourceCache resources, ViewOutput output, bool fxaa)
    {
        output.BindFinal();
        if (fxaa)
            _fxaa.Run(resources, output.Graded);
        else
            // alphaSource makes this an alpha passthrough rather than RunRaw's default of opaque.
            _present.RunRaw(resources, output.Graded, 1f, alphaSource: output.Graded);
    }

    // The texture and brightness scale of a diagnostic view, or null for the graded ones.
    static (GpuTexture Texture, float Scale)? RawView(SceneViewMode mode, RenderTargets targets, FrameResult frame) => mode switch
    {
        SceneViewMode.Albedo => (frame.Albedo, 1f),
        SceneViewMode.Normal => (frame.Normal, 1f),
        SceneViewMode.Shadow => (frame.PreShadow, 1f),
        SceneViewMode.AmbientOcclusion => (frame.PreMisc, 1f),
        SceneViewMode.Emission => (targets.GBuffer[5], 1f),
        SceneViewMode.GBuffer0 => (targets.GBuffer[0], 1f),
        SceneViewMode.GBuffer2 => (targets.GBuffer[2], 1f),
        SceneViewMode.GBuffer4 => (targets.GBuffer[4], 1f),
        SceneViewMode.LinearDepth => (targets.LinearDepth, 1f),
        SceneViewMode.FieldLightLayer0 => (targets.LayerCopy(targets.FieldLightPrePassArray, 0), 1f),
        SceneViewMode.FieldLightLayer1 => (targets.LayerCopy(targets.FieldLightPrePassArray, 1), 1f),
        SceneViewMode.CharaLightLayer0 => (targets.LayerCopy(targets.LightPrePassArray, 0), 1f),
        SceneViewMode.LastResolve => (targets.ResolvePass, LastResolveScale),
        SceneViewMode.PassId => (frame.PassId, PassIdScale),
        _ => null,
    };

    // The HDR texture an HDR view shows, or null for the tonemapped beauty view.
    static GpuTexture? HdrView(SceneViewMode mode, RenderTargets targets, FrameResult frame) => mode switch
    {
        SceneViewMode.HdrPreview => frame.Final,
        SceneViewMode.AfterResolve or SceneViewMode.AfterForward or SceneViewMode.AfterFlare =>
            targets.Stage(mode - SceneViewMode.AfterResolve) ?? frame.Final,
        SceneViewMode.ResolvePass => targets.Stage(3) ?? frame.Final,
        _ => null,
    };
}
