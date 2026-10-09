using WildRenderingSharp.Assets;
using WildRenderingSharp.Graphics;
using WildRenderingSharp.Pipeline;
using WildRenderingSharp.Pipeline.Frame;

namespace WildRenderingSharp.Profiles.Botw.Stages;

/// <summary>Exposure and a soft shoulder, from the HDR target to the display-range one.</summary>
public sealed class BotwTonemapStage(StageServices services) : IFrameStage, IDisposable
{
    readonly uint _program = GLProgramBuilder.Build(services.Gl, FullscreenShaders.Vertex450, GlslFiles.Load("Botw/Tonemap.frag"), "botw_tonemap");

    public void Run(FrameContext frame)
    {
        var gl = services.Gl;
        frame.Targets.BindColorTarget(frame.Targets.Ldr);
        gl.UseProgram(_program);
        gl.BindTextureUniform(_program, "t", 0, frame.Targets.Final.Handle);
        gl.SetFloat(_program, "uExposure", frame.Lighting.Exposure);
        services.Resources.DrawFullscreenTriangle();
        GLDiagnostics.CheckPass(gl, "BotW tonemap");
    }

    public void Dispose() => services.Gl.DeleteProgram(_program);
}
