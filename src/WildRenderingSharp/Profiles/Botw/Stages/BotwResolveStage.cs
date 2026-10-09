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
public sealed class BotwResolveStage(StageServices services, BotwPasses passes, BotwLightingStage lighting) : IFrameStage, IDisposable
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
        gl.BindTextureUniform(_flip, "tEmission", 1, targets.GBuffer[BotwGBuffer.Emission].Handle);
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
        passes.BindAt(BotwSamplers.Shading.Albedo, targets.GBuffer[BotwGBuffer.Albedo].Handle);
        passes.BindAt(BotwSamplers.Shading.Normal, targets.GBuffer[BotwGBuffer.Normal].Handle);
        passes.BindAt(BotwSamplers.Shading.LinearDepth, targets.LinearDepth.Handle);
        passes.BindAt(BotwSamplers.Shading.Shadow, pre.Texture(lighting.Shadow));
        passes.BindAt(BotwSamplers.Shading.PreFog, pre.Texture(lighting.Fog));
        passes.BindAt(BotwSamplers.Shading.RenderDepth, targets.GBufferDepth.Handle);
        passes.BindAt(BotwSamplers.Shading.LightPrePass, pre.Array, TextureTarget.Texture2DArray);
        passes.BindAt(BotwSamplers.Shading.HalfDepth, targets.LinearDepthHalf.Handle);
        passes.BindAt(BotwSamplers.IdTexture, targets.GBuffer[BotwGBuffer.MaterialId].Handle);
        passes.BindAt(BotwSamplers.LightAnalyzed, passes.LightAnalyzed);
    }

    public void Dispose() => services.Gl.DeleteProgram(_flip);
}
