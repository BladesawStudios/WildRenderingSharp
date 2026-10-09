using System.Numerics;
using Silk.NET.OpenGL;
using WildRenderingSharp.Gpu;
using WildRenderingSharp.Graphics.Ubos;
using WildRenderingSharp.Pipeline.Passes;
using WildRenderingSharp.Pipeline.Resources;
using WildRenderingSharp.Pipeline.Targets;
using WildRenderingSharp.Profiles.Totk.Ubos;
using WildRenderingSharp.Shaders;

namespace WildRenderingSharp.Profiles.Totk.Deferred.Resolve;

/// <summary>
/// Runs each distinct deferred resolve program the loaded models need: an unmodified game shader per pass over a fullscreen quad it
/// synthesises from <c>gl_VertexID</c>, composited into the running result only where the pass-ID mask names it.
/// </summary>
internal sealed class DeferredResolvePass : IDisposable
{
    const int CubeEnvMapUnit = 9;

    static readonly string ComposeFragmentSource = GlslFiles.Load("Totk/Deferred/DeferredResolve/Compose.frag");

    readonly GL _gl;
    readonly uint _composeProgram;
    readonly uint _texPreFog, _texVolumeMask;
    readonly CubeEnvironment _cubeEnvironment;
    readonly TileFlagsBuffer _tileFlags;

    string? _tracePass;
    Func<ResolvedDeferredPass, ResolveTrace>? _traceStart;

    public DeferredResolvePass(GL gl)
    {
        _gl = gl;
        _composeProgram = GLProgramBuilder.Build(gl, FullscreenShaders.Vertex450, ComposeFragmentSource, "mask_compose");
        _texPreFog = ConstantTextures.Rgba(gl, 0f, 0f, 0f, 0f);
        _texVolumeMask = ConstantTextures.Rgba(gl, 0f, 0f, 0f, 0f);
        _cubeEnvironment = new CubeEnvironment(gl);
        _tileFlags = new TileFlagsBuffer(gl);
    }

    // The pass whose own output is kept for the debug views, or -1.
    public int DebugPass { get; set; } = -1;

    internal void Trace(string pass, Func<ResolvedDeferredPass, ResolveTrace> start)
    {
        _tracePass = pass;
        _traceStart = start;
    }

    public void SetEnvironment(Vector3 sky, Vector3 ground) => _cubeEnvironment.Set(sky, ground);

    public void Run(GLResourceCache resources, RenderTargets targets, IReadOnlyList<ResolvedDeferredPass> passes,
        float emissionScale, float sceneGain, float exposure, Func<string, bool>? include = null, int claimEmptyPass = -1)
    {
        // Final already holds the background, and the compose step only touches pixels its pass-ID mask claims, so an uncovered pixel keeps it.
        targets.BindColorTarget(targets.Final);
        _gl.Disable(EnableCap.DepthTest);

        foreach (var pass in passes)
        {
            if (include is not null && !include(pass.Name))
                continue;

            DrawPass(resources, targets, pass);
            Compose(resources, targets, pass, emissionScale, sceneGain, exposure, claimEmptyPass);
        }
    }

    public void Dispose()
    {
        _gl.ReleaseProgram(_composeProgram);
        _gl.DeleteTexture(_texPreFog);
        _gl.DeleteTexture(_texVolumeMask);
        _cubeEnvironment.Dispose();
        _tileFlags.Dispose();
    }

    // Runs the game's resolve program for one pass over the whole screen into the ResolvePass target.
    void DrawPass(GLResourceCache resources, RenderTargets targets, ResolvedDeferredPass pass)
    {
        // Rebound every pass: the compose step claims units 0 and 1 (G-buffer albedo and normal), which would otherwise be the previous pass's output.
        BindResolveInputs(targets);
        _gl.BindTextureAt(28, (pass.FieldLights ? targets.FieldLightPrePassArray : targets.LightPrePassArray).Handle, TextureTarget.Texture2DArray);
        resources.BindMaterial(pass.Material);

        targets.BindColorTarget(targets.ResolvePass);
        _gl.ClearColor(0, 0, 0, 1);
        _gl.Clear(ClearBufferMask.ColorBufferBit);

        var trace = StartTrace(pass);
        _gl.UseProgram(trace?.Program ?? pass.Program);
        trace?.Bind();
        if (pass.Tiled)
        {
            resources.BindCamera(TotkUniformKeys.FieldCamera);
            _gl.BindBufferBase(BufferTargetARB.ShaderStorageBuffer, 0, _tileFlags.Handle);
        }
        resources.DrawFullscreenQuadStrip();
        if (pass.Tiled)
            resources.BindCamera(FrameUniformKeys.SceneCamera);
        if (trace is not null)
        {
            trace.Finish();
            trace.Dispose();
        }
        if (pass.PassIndex == DebugPass)
            targets.SnapshotResolve();
    }

    ResolveTrace? StartTrace(ResolvedDeferredPass pass)
    {
        if (_tracePass != pass.Name || _traceStart is null || pass.Source.Length == 0)
            return null;
        _tracePass = null;
        return _traceStart(pass);
    }

    // Blends the pass's output into the running result wherever the pass-ID mask names it.
    void Compose(GLResourceCache resources, RenderTargets targets, ResolvedDeferredPass pass,
        float emissionScale, float sceneGain, float exposure, int claimEmptyPass)
    {
        targets.BindColorTarget(targets.Final);
        _gl.UseProgram(_composeProgram);
        _gl.BindTextureUniform(_composeProgram, "t", 0, targets.ResolvePass.Handle);
        _gl.BindTextureUniform(_composeProgram, "tex_id", 1, targets.PassId.Handle);
        _gl.BindTextureUniform(_composeProgram, "tex_emis", 20, targets.GBuffer[5].Handle);
        _gl.BindTextureUniform(_composeProgram, "tex_alb", 21, targets.GBuffer[1].Handle);
        _gl.BindTextureUniform(_composeProgram, "tex_gdepth", 22, targets.GBufferDepth.Handle);
        _gl.SetFloat(_composeProgram, "uEmission", emissionScale);
        _gl.SetFloat(_composeProgram, "uEmissionExposureRcp", exposure > 1e-4f ? 1f / exposure : 1f);
        _gl.SetFloat(_composeProgram, "uSceneGain", sceneGain);
        _gl.SetInt(_composeProgram, "uGFlip", 1);
        _gl.SetFloat(_composeProgram, "uId", (pass.PassIndex + 1) / 255f);
        _gl.SetInt(_composeProgram, "uAll", 0);
        _gl.SetInt(_composeProgram, "uClaimEmpty", pass.PassIndex == claimEmptyPass ? 1 : 0);
        resources.DrawFullscreenTriangle();
    }

    // Units and semantics are fixed by the game's compiled resolve shaders; some inputs are authentic, others neutral stand-ins.
    void BindResolveInputs(RenderTargets targets)
    {
        _gl.BindTextureAt(0, targets.GBuffer[1].Handle);       // cTex_GBuffAlbedo
        _gl.BindTextureAt(1, targets.GBuffer[3].Handle);       // cTex_GBuffNormal
        _gl.BindTextureAt(2, targets.GBuffer[0].Handle);       // cTex_GBuffMaterialID, attachment 0 as the G-buffer programs write it
        _gl.BindTextureAt(4, targets.LinearDepth.Handle);      // cTex_NormalizedLinearDepth
        _gl.BindTextureAt(5, targets.LinearDepthHalf.Handle);  // cTex_HalfNormalizedLinearDepth
        _gl.BindTextureAt(11, _texVolumeMask);                 // cTex_VolumeMask (neutral; Env[81] carries the palette's real tint)
        _gl.BindTextureAt(16, targets.PreShadow.Handle);       // cTex_PreShadow (synthesised)
        _gl.BindTextureAt(17, _texPreFog);                     // cTex_PreFog (neutral; no aerial perspective)
        _gl.BindTextureAt(18, targets.PreMisc.Handle);         // cTex_PreMisc (synthesised)
        _gl.BindTextureAt(28, targets.LightPrePassArray.Handle, TextureTarget.Texture2DArray); // cTex_DeferredLightPrePass
        _gl.BindTextureAt(CubeEnvMapUnit, _cubeEnvironment.Handle, TextureTarget.TextureCubeMap); // cTex_CubeEnvMap (field_water)
    }
}
