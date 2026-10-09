using WildRenderingSharp.Gpu;
using WildRenderingSharp.Pipeline.Resources;
using WildRenderingSharp.Pipeline.Targets;
using WildRenderingSharp.Shaders;
using System.Numerics;
using Silk.NET.OpenGL;

namespace WildRenderingSharp.Pipeline.Passes;

/// <summary>
/// Produces layer 0 of <c>cTex_DeferredLightPrePass</c>, the light-accumulation buffer the <c>chara_*</c> resolve shaders sample for their
/// main light colour.
/// </summary>
internal sealed class LightPrePass : IDisposable
{
    readonly GL _gl;
    readonly uint _program;

    // decodeGBuffNormal/viewPos are a direct copy of ScreenSpaceShadowAndAoPass's own (already
    // real-game-verified) versions - same packed-normal encoding, same linear-depth reconstruction.
    static readonly string FragmentSource = GlslFiles.Load("Pipeline/LightPrePass/Main.frag");

    public LightPrePass(GL gl)
    {
        _gl = gl;
        _program = GLProgramBuilder.Build(gl, FullscreenShaders.Vertex450, FragmentSource, "light_prepass");
    }

    public readonly record struct Params(
        Matrix4x4 ViewInv, Vector2 TanHalf, float Near, float Far,
        Vector3 SunWorld, Vector3 SunColor, Vector3 HemiSky, Vector3 HemiGround, bool Synthetic = true);

    public void Run(GLResourceCache resources, RenderTargets targets, Params p)
    {
        // What the field programs read: ambient only, as the sun is theirs to compute.
        Draw(resources, targets, p, targets.FieldLightPrePassArray, direct: 0f);
        // Layer 1 is added to the field programs' colour as it stands and nothing writes it, so it is black every frame.
        ClearLayer(targets, targets.FieldLightPrePassArray, 1);
        ClearLayer(targets, targets.LightPrePassArray, 1);

        if (!p.Synthetic)
        {
            // No local lights: the buffer the game accumulates them in is black.
            targets.BindColorTargetLayer(targets.LightPrePassArray, layer: 0);
            _gl.ClearColor(0f, 0f, 0f, 0f);
            _gl.Clear(ClearBufferMask.ColorBufferBit);
            return;
        }
        Draw(resources, targets, p, targets.LightPrePassArray, direct: 1f);
    }

    void ClearLayer(RenderTargets targets, GpuTexture array, int layer)
    {
        targets.BindColorTargetLayer(array, layer);
        _gl.ColorMask(true, true, true, true);
        _gl.ClearColor(0f, 0f, 0f, 0f);
        _gl.Clear(ClearBufferMask.ColorBufferBit);
    }

    void Draw(GLResourceCache resources, RenderTargets targets, Params p, GpuTexture array, float direct)
    {
        _gl.Disable(EnableCap.DepthTest);
        _gl.UseProgram(_program);
        _gl.SetMat4(_program, "uViewInv", p.ViewInv);
        _gl.Uniform2(_gl.UniformLocation(_program, "uTanHalf"), p.TanHalf.X, p.TanHalf.Y);
        _gl.Uniform1(_gl.UniformLocation(_program, "uNear"), p.Near);
        _gl.Uniform1(_gl.UniformLocation(_program, "uFar"), p.Far);
        _gl.Uniform3(_gl.UniformLocation(_program, "uSunWorld"), p.SunWorld.X, p.SunWorld.Y, p.SunWorld.Z);
        _gl.Uniform3(_gl.UniformLocation(_program, "uSunColor"), p.SunColor.X, p.SunColor.Y, p.SunColor.Z);
        _gl.Uniform3(_gl.UniformLocation(_program, "uHemiSky"), p.HemiSky.X, p.HemiSky.Y, p.HemiSky.Z);
        _gl.Uniform3(_gl.UniformLocation(_program, "uHemiGround"), p.HemiGround.X, p.HemiGround.Y, p.HemiGround.Z);
        _gl.Uniform1(_gl.UniformLocation(_program, "uDirect"), direct);

        targets.BindColorTargetLayer(array, layer: 0);
        _gl.BindTextureUniform(_program, "tex_nld", 0, targets.LinearDepth.Handle);
        _gl.BindTextureUniform(_program, "tex_gnrm", 1, targets.GBuffer[3].Handle);
        resources.DrawFullscreenTriangle();
    }

    public void Dispose() => _gl.ReleaseProgram(_program);
}
