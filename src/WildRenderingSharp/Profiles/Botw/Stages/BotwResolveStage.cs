using System.Numerics;
using Silk.NET.OpenGL;
using WildRenderingSharp.Assets;
using WildRenderingSharp.Graphics;
using WildRenderingSharp.Pipeline;
using WildRenderingSharp.Pipeline.Frame;

namespace WildRenderingSharp.Profiles.Botw.Stages;

/// <summary>
/// Runs the game's own character shading passes over the G-buffer and the lighting buffers before them. Each draws one fullscreen
/// triangle for the material ID it lights; the game picks its pixels with the depth test, which is replaced here by a mask on the G-buffer's
/// ID channel.
/// </summary>
public sealed class BotwResolveStage(FrameServices services, BotwPasses passes, BotwLightingStage lighting) : IFrameStage, IDisposable
{
    static readonly string[] CharacterPasses =
        ["chara_nonmetal", "chara_nonmetal_direct", "chara_metal", "chara_grossy", "chara_hair", "chara_skin", "chara_eye"];

    readonly uint _flip = GLProgramBuilder.Build(services.Gl, FullscreenShaders.Vertex450, GlslFiles.Load("Botw/Flip.frag"), "botw_flip");
    List<BotwPass>? _passes;

    public void Run(FrameContext frame)
    {
        var gl = services.Gl;
        var targets = frame.Targets;
        var resources = services.Resources;
        var environment = frame.Environment as BotwEnvironment;
        _passes ??= [.. CharacterPasses.Select(name => passes.Load(name)).OfType<BotwPass>()];

        Clear(targets, targets.ResolvePass, new Vector4(environment?.Background ?? default, 1f));
        targets.BindColorTarget(targets.ResolvePass);
        gl.Disable(EnableCap.DepthTest);
        gl.Disable(EnableCap.CullFace);
        gl.Disable(EnableCap.Blend);
        resources.BindCamera(FrameUniformKeys.SceneCamera);
        resources.BindEnvironment();
        resources.BindUbo(FrameUniformKeys.SceneMaterial, BotwBindings.SceneMaterial);

        ClipOrigin.Game(gl, true);
        foreach (var pass in _passes)
        {
            BindInputs(targets);
            passes.Draw(pass);
        }
        ClipOrigin.Game(gl, false);
        GLDiagnostics.CheckPass(gl, "BotW deferred shading");

        targets.BindColorTarget(targets.Final);
        gl.UseProgram(_flip);
        int debug = environment?.DebugPreShading ?? -1;
        gl.BindTextureUniform(_flip, "t", 0, debug >= 0 ? lighting.PreShading.Texture(debug) : targets.ResolvePass.Handle);
        gl.BindTextureUniform(_flip, "tEmission", 1, targets.GBuffer[5].Handle);
        resources.DrawFullscreenTriangle();
        gl.ActiveTexture(TextureUnit.Texture0);
        GLDiagnostics.CheckPass(gl, "BotW resolve");
    }

    void Clear(RenderTargets targets, GpuTexture target, Vector4 color)
    {
        targets.BindColorTarget(target);
        services.Gl.ClearColor(color.X, color.Y, color.Z, color.W);
        services.Gl.Clear(ClearBufferMask.ColorBufferBit);
    }

    void BindInputs(RenderTargets targets)
    {
        var pre = lighting.PreShading;
        passes.BindAt(0, targets.GBuffer[1].Handle);
        passes.BindAt(1, targets.GBuffer[3].Handle);
        passes.BindAt(3, targets.LinearDepth.Handle);
        passes.BindAt(5, pre.Texture(lighting.Shadow));
        passes.BindAt(7, pre.Texture(lighting.Fog));
        passes.BindAt(8, targets.GBufferDepth.Handle);
        passes.BindAt(13, pre.Array, TextureTarget.Texture2DArray);
        passes.BindAt(14, targets.LinearDepthHalf.Handle);
        passes.BindAt(BotwPasses.IdUnit, targets.GBuffer[0].Handle);
        passes.BindAt(BotwPasses.LightAnalyzedUnit, passes.LightAnalyzed);
    }

    public void Dispose() => services.Gl.DeleteProgram(_flip);
}
