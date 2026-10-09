using WildRenderingSharp.Assets;
using Silk.NET.OpenGL;
using WildRenderingSharp.Graphics;
using WildRenderingSharp.Pipeline;
using WildRenderingSharp.Pipeline.Frame;

namespace WildRenderingSharp.Profiles.Botw.Stages;

/// <summary>Lights the G-buffer with the frame's sun and hemisphere into the HDR target.</summary>
public sealed class BotwResolveStage(FrameServices services) : IFrameStage, IDisposable
{
    readonly uint _program = GLProgramBuilder.Build(services.Gl, FullscreenShaders.Vertex450, GlslFiles.Load("Botw/Resolve.frag"), "botw_resolve");

    public void Run(FrameContext frame)
    {
        var gl = services.Gl;
        var targets = frame.Targets;
        var environment = frame.Environment as BotwEnvironment;

        gl.Disable(EnableCap.DepthTest);
        targets.BindColorTarget(targets.Final);
        gl.UseProgram(_program);
        gl.BindTextureUniform(_program, "tAlbedo", 0, targets.GBuffer[1].Handle);
        gl.BindTextureUniform(_program, "tNormal", 1, targets.GBuffer[3].Handle);
        gl.BindTextureUniform(_program, "tDepth", 2, targets.GBufferDepth.Handle);
        gl.SetVec3(_program, "uSunView", frame.SunView);
        gl.SetVec3(_program, "uSunColor", frame.SunColor);
        gl.SetVec3(_program, "uHemiSky", frame.HemiSky);
        gl.SetVec3(_program, "uHemiGround", frame.HemiGround);
        gl.SetVec3(_program, "uBackground", environment?.Background ?? default);
        gl.SetInt(_program, "uFlip", frame.GameOrigin ? 1 : 0);
        gl.SetInt(_program, "uView", environment?.ViewMode ?? 0);
        services.Resources.DrawFullscreenTriangle();
        gl.ActiveTexture(TextureUnit.Texture0);
        GLDiagnostics.CheckPass(gl, "BotW resolve");
    }

    public void Dispose() => services.Gl.DeleteProgram(_program);
}
