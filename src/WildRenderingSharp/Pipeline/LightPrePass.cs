using System.Numerics;
using Silk.NET.OpenGL;

namespace WildRenderingSharp.Pipeline;

/// <summary>
/// Produces layer 0 of <c>cTex_DeferredLightPrePass</c> (binding 28, <c>sampler2DArray</c>) - the real light-accumulation buffer
/// every <c>chara_*</c> deferred resolve shader samples for its own main light colour (immediately converted to a luminance value
/// that drives further shading - confirmed by reading chara_metal/chara_skin/chara_nonmetal/chara_grossy directly, all four
/// identical: <c>texture(cTex_DeferredLightPrePass, vec3(u, v, 0)).xyz</c>). Was a flat (0,0,0,0) constant before this pass existed
/// - every material sampling it got literally zero light, independent of anything else WildRenderingSharp computed, which is very
/// likely the dominant reason the whole pipeline needed a blanket 10x exposure crutch (see <c>DeferredResolvePass.Run</c>'s own
/// remarks: "no preshading passes, no cubemap IBL, no light pre-pass"). HONEST SCOPE: this is a genuine, if deliberately
/// simplified, stand-in for that missing pre-pass - a single directional sun term plus a hemisphere ambient lerp, using the SAME
/// real palette data (<c>sunColor</c>/<c>hemiSky</c>/<c>hemiGround</c>) every other real light source in this pipeline already
/// uses. It is NOT the real game's light pre-pass: the genuine one also accumulates up to ~144 real per-region local point/area
/// lights (<c>LocationalLightRig</c> entries found in TotK's own <c>envobj/master_field.baglenv</c>) and real dynamic
/// reflection-probe cubemap IBL (<c>envobj/common.baglcube</c> - confirmed a runtime-rendered probe system, not a static asset
/// WildRenderingSharp can just load). Neither of those is implemented here - this narrows the gap from "identically zero" to "a
/// plausible directional-plus-ambient estimate," not to parity.
/// </summary>
public sealed class LightPrePass : IDisposable
{
    readonly GL _gl;
    readonly uint _program;

    // decodeGBuffNormal/viewPos are a direct copy of ScreenSpaceShadowAndAoPass's own (already
    // real-game-verified) versions - same packed-normal encoding, same linear-depth reconstruction.
    const string FragmentSource = """
        #version 450 core
        uniform sampler2D tex_nld;
        uniform sampler2D tex_gnrm;
        uniform mat4 uViewInv;
        uniform vec2 uTanHalf;
        uniform float uNear, uFar;
        uniform vec3 uSunWorld;   // direction TOWARD the sun, world space
        uniform vec3 uSunColor, uHemiSky, uHemiGround;
        in vec2 vUV; out vec4 fragColor;

        vec3 viewPos(vec2 uv) {
            float z = texture(tex_nld, uv).r * (uFar - uNear) + uNear;
            return vec3((uv.x * 2.0 - 1.0) * uTanHalf.x * z, (1.0 - uv.y * 2.0) * uTanHalf.y * z, -z);
        }

        vec3 decodeGBuffNormal(vec2 uv) {
            vec4 g = texture(tex_gnrm, uv);
            int zb = int(trunc(g.z * 255.0));
            if ((zb & 8) == 0) return vec3(0.0, 0.0, 1.0);
            float sx = ((zb & 2) != 0) ? 1.0 : -1.0;
            float sy = ((zb & 1) != 0) ? 1.0 : -1.0;
            float u2 = g.x * g.x + g.y * g.y;
            float zHalf = sqrt(max(0.0, 1.0 - u2));
            vec3 n = vec3(g.x * zHalf * sx * 2.0, g.y * zHalf * sy * 2.0, 1.0 - 2.0 * u2);
            float len2 = dot(n, n);
            return (len2 > 1e-6) ? n * inversesqrt(len2) : vec3(0.0, 0.0, 1.0);
        }

        void main() {
            float d = texture(tex_nld, vUV).r;
            if (d >= 0.999) { fragColor = vec4(uHemiSky, 1.0); return; }
            vec3 nView = decodeGBuffNormal(vUV);
            vec3 nWorld = normalize(mat3(uViewInv) * nView);
            vec3 ambient = mix(uHemiGround, uHemiSky, nWorld.z * 0.5 + 0.5); // the renderer's world is Z-up
            vec3 direct = uSunColor * max(0.0, dot(nWorld, uSunWorld));
            fragColor = vec4(ambient + direct, 1.0);
        }
        """;

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
