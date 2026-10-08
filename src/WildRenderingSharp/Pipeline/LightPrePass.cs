using WildRenderingSharp.Graphics;
using System.Numerics;
using Silk.NET.OpenGL;

namespace WildRenderingSharp.Pipeline;

/// <summary>
/// Produces layer 0 of <c>cTex_DeferredLightPrePass</c> (binding 28, <c>sampler2DArray</c>) - the real light-accumulation buffer
/// every <c>chara_*</c> deferred resolve shader samples for its own main light colour (immediately converted to a luminance value
/// that drives further shading - confirmed by reading chara_metal/chara_skin/chara_nonmetal/chara_grossy directly, all four
/// identical: <c>texture(cTex_DeferredLightPrePass, vec3(u, v, 0)).xyz</c>).
/// </summary>
public sealed class LightPrePass : IDisposable
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
        Vector4[] ViewInv3Rows, Vector2 TanHalf, float Near, float Far,
        Vector3 SunWorld, Vector3 SunColor, Vector3 HemiSky, Vector3 HemiGround, bool Synthetic = true);

    public void Run(GLResourceCache resources, RenderTargets targets, Params p)
    {
        if (!p.Synthetic)
        {
            // No local lights: the buffer the game accumulates them in is black.
            targets.BindColorTargetLayer(targets.LightPrePassArray, layer: 0);
            _gl.ClearColor(0f, 0f, 0f, 0f);
            _gl.Clear(ClearBufferMask.ColorBufferBit);
            return;
        }
        _gl.Disable(EnableCap.DepthTest);
        _gl.UseProgram(_program);
        SetMat4(_program, "uViewInv", Rendering.Mat4Math.ToMat4(p.ViewInv3Rows));
        _gl.Uniform2(_gl.GetUniformLocation(_program, "uTanHalf"), p.TanHalf.X, p.TanHalf.Y);
        _gl.Uniform1(_gl.GetUniformLocation(_program, "uNear"), p.Near);
        _gl.Uniform1(_gl.GetUniformLocation(_program, "uFar"), p.Far);
        _gl.Uniform3(_gl.GetUniformLocation(_program, "uSunWorld"), p.SunWorld.X, p.SunWorld.Y, p.SunWorld.Z);
        _gl.Uniform3(_gl.GetUniformLocation(_program, "uSunColor"), p.SunColor.X, p.SunColor.Y, p.SunColor.Z);
        _gl.Uniform3(_gl.GetUniformLocation(_program, "uHemiSky"), p.HemiSky.X, p.HemiSky.Y, p.HemiSky.Z);
        _gl.Uniform3(_gl.GetUniformLocation(_program, "uHemiGround"), p.HemiGround.X, p.HemiGround.Y, p.HemiGround.Z);

        targets.BindColorTargetLayer(targets.LightPrePassArray, layer: 0);
        BindTexture(_program, "tex_nld", 0, targets.LinearDepth.Handle);
        BindTexture(_program, "tex_gnrm", 1, targets.GBuffer[3].Handle);
        resources.DrawFullscreenTriangle();
    }

    void BindTexture(uint program, string uniform, int unit, uint textureHandle)
    {
        _gl.ActiveTexture(TextureUnit.Texture0 + unit);
        _gl.BindTexture(TextureTarget.Texture2D, textureHandle);
        _gl.Uniform1(_gl.GetUniformLocation(program, uniform), unit);
    }

    void SetMat4(uint program, string name, Vector4[] rows)
    {
        Span<float> flat = stackalloc float[16];
        for (int i = 0; i < 4; i++)
        {
            flat[i * 4 + 0] = rows[i].X;
            flat[i * 4 + 1] = rows[i].Y;
            flat[i * 4 + 2] = rows[i].Z;
            flat[i * 4 + 3] = rows[i].W;
        }
        _gl.UniformMatrix4(_gl.GetUniformLocation(program, name), 1, true, flat);
    }

    public void Dispose() => _gl.DeleteProgram(_program);
}
