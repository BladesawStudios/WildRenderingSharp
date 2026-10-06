using System.Numerics;
using Silk.NET.OpenGL;

namespace WildRenderingSharp.Pipeline;

/// <summary>
/// Produces the two screen-space buffers the real deferred resolve shaders expect from the
/// (unimplemented) <c>preshading_*</c> passes - <c>cTex_PreShadow</c> (sun visibility, 12-tap
/// Poisson-disc PCF against the shadow map) and <c>cTex_PreMisc</c> (an alchemy-style AO plus the
/// diffuse N.L the <c>chara_*</c> resolve passes read out of <c>.y</c> rather than computing
/// themselves). Both decode the packed G-buffer normal with the same Lambert-azimuthal scheme the
/// game's own shaders write and read. HONEST SCOPE: these two textures are produced by WildRenderingSharp, not
/// the game - only their CONSUMPTION (the real deferred resolve shader) is authentic. Direct port
/// of <c>PRESHADOW_SRC</c>/<c>AO_SRC</c>/<c>AO_BLUR_SRC</c>.
/// </summary>
public sealed class ScreenSpaceShadowAndAoPass : IDisposable
{
    readonly GL _gl;
    readonly uint _preshadowProgram, _preshadingFilterProgram, _preshadingReduceProgram, _preshadingUpsampleProgram, _aoProgram, _blurProgram;

    public const float AoRadiusWorld = 0.035f;
    public const float AoStrength = 0.85f;
    public const float ShadowBiasWorld = 0.0015f;

    const string QuadVertexSource = """
        #version 450 core
        out vec2 vUV;
        void main() {
            float x = -1.0 + float((gl_VertexID & 1) * 4);
            float y = -1.0 + float((gl_VertexID & 2) * 2);
            vUV = vec2(x, y) * 0.5 + 0.5;
            gl_Position = vec4(x, y, 0.0, 1.0);
        }
        """;

    const string PreshadowFragmentSource = """
        #version 450 core
        uniform sampler2D tex_nld;
        uniform sampler2DShadow tex_shadow;
        uniform sampler2D tex_gnrm;     // cTex_GBuffNormal, same space as tex_nld
        uniform mat4 uViewInv;          // Context decl 3 (cViewInv), promoted to 4x4
        uniform mat4 uLightViewProj;
        uniform vec2 uTanHalf;          // Context decl 7 = (tanHalfFovX, tanHalfFovY)
        uniform vec3 uSunWorld;         // direction TOWARD the sun, world space
        uniform float uNear, uFar, uBias, uTexel, uTexelWorld, uDepthRange;
        // Cascades (FrameRequest.ShadowCascades): nested regions, finest first, one layer each.
        uniform sampler2DArrayShadow tex_cascades;
        uniform int uCascadeCount;
        uniform mat4 uCascadeViewProj[4];
        uniform vec4 uCascadeParams[4];   // x: texel size in metres, y: bias in light depth, z: PCF radius in texels
        uniform float uCascadeTexel;
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
            if (d >= 0.999) { fragColor = vec4(1.0, 0.0, 1.0, 1.0); return; }
            vec3 p = viewPos(vUV);
            vec4 world = uViewInv * vec4(p, 1.0);

            vec3 nView = decodeGBuffNormal(vUV);
            vec3 nWorld = normalize(mat3(uViewInv) * nView);

            float ndl = dot(nWorld, uSunWorld);

            if (uCascadeCount > 0) {
                vec3 nPerpC = nWorld - uSunWorld * ndl;
                float grazing = 2.8 * pow(1.0 - clamp(abs(ndl), 0.0, 1.0), 2.0);
                ivec2 ppC = ivec2(mod(gl_FragCoord.xy, 4.0));
                const float bayerC[16] = float[16](0.0, 8.0, 2.0, 10.0, 12.0, 4.0, 14.0, 6.0, 3.0, 11.0, 1.0, 9.0, 15.0, 7.0, 13.0, 5.0);
                float angleC = bayerC[ppC.y * 4 + ppC.x] / 16.0 * 6.28318530718;
                float caC = cos(angleC), saC = sin(angleC);
                for (int c = 0; c < uCascadeCount; c++) {
                    vec4 prm = uCascadeParams[c];
                    vec3 wb = world.xyz + nWorld * prm.x + nPerpC * (prm.x * grazing);
                    vec4 lcC = uCascadeViewProj[c] * vec4(wb, 1.0);
                    vec3 scC = lcC.xyz / lcC.w * 0.5 + 0.5;
                    // Inside this cascade with room for the filter, or try the next, coarser one.
                    float margin = uCascadeTexel * (prm.z + 2.0);
                    if (any(lessThan(scC.xy, vec2(margin))) || any(greaterThan(scC.xy, vec2(1.0 - margin))) || scC.z >= 1.0)
                        continue;
                    float refC = clamp(scC.z - prm.y, 0.0, 1.0);
                    const vec2 discC[8] = vec2[8](
                        vec2(-0.94201624, -0.39906216), vec2( 0.94558609, -0.76890725),
                        vec2(-0.09418410, -0.92938870), vec2( 0.34495938,  0.29387760),
                        vec2(-0.91588581,  0.45771432), vec2(-0.81544232, -0.87912464),
                        vec2(-0.38277543,  0.27676845), vec2( 0.97484398,  0.75648379));
                    float visC = 0.0;
                    float rC = uCascadeTexel * prm.z;
                    for (int i = 0; i < 8; i++) {
                        vec2 s = vec2(discC[i].x * caC - discC[i].y * saC, discC[i].x * saC + discC[i].y * caC) * rC;
                        visC += texture(tex_cascades, vec4(scC.xy + s, float(c), refC));
                    }
                    visC *= 0.125;
                    fragColor = vec4(visC, 0.0, 1.0, visC);
                    return;
                }
                // Past the last cascade: unshadowed, as the game is beyond its own.
                fragColor = vec4(1.0, 0.0, 1.0, 1.0);
                return;
            }

            // Normal-offset bias from preshading_chara prog 120:
            // baseOffset = 1.0 * uTexelWorld (prevents self-shadow at all angles)
            // grazingOffset = 2.8 * uTexelWorld * (1 - |ndl|)^2 (grazing-angle expansion)
            vec3 nPerp = nWorld - uSunWorld * ndl;
            float absNdl = clamp(abs(ndl), 0.0, 1.0);
            float grazingOffset = uTexelWorld * (2.8 * pow(1.0 - absNdl, 2.0));
            vec3 worldBiased = world.xyz + nWorld * uTexelWorld + nPerp * grazingOffset;

            vec4 lc = uLightViewProj * vec4(worldBiased, 1.0);
            vec3 sc = lc.xyz / lc.w * 0.5 + 0.5;
            if (any(lessThan(sc.xy, vec2(0.0))) || any(greaterThan(sc.xy, vec2(1.0)))) {
                fragColor = vec4(1.0, 0.0, 1.0, 1.0);
                return;
            }

            float bias = uBias / uDepthRange;
            float refZ = clamp(sc.z - bias, 0.0, 1.0);

            // TotK uses cascades with fine per-cascade texels; WildRenderingSharp uses a single wide-frustum
            // ortho map. The real 4-tap PCF at ±0.5 texel produces only 5 discrete output values
            // (0, 0.25, 0.5, 0.75, 1.0) on WildRenderingSharp's coarser map, and chara_skin's 8× ramp turns
            // those into hard staircases. Fix: 16-tap rotated Poisson disc PCF over ±3 shadow
            // texels to produce a smooth 0→1 gradient that the downstream Gaussian can work with.
            //
            // Disc rotated per-pixel via Bayer 4×4 to break fixed-grid sampling patterns.
            const vec2 disc[16] = vec2[16](
                vec2(-0.94201624, -0.39906216), vec2( 0.94558609, -0.76890725),
                vec2(-0.09418410, -0.92938870), vec2( 0.34495938,  0.29387760),
                vec2(-0.91588581,  0.45771432), vec2(-0.81544232, -0.87912464),
                vec2(-0.38277543,  0.27676845), vec2( 0.97484398,  0.75648379),
                vec2( 0.44323325, -0.97511554), vec2( 0.53742981, -0.47373420),
                vec2(-0.26496911, -0.41893023), vec2( 0.79197514,  0.19090188),
                vec2(-0.24188840,  0.99706507), vec2(-0.81409955,  0.91437590),
                vec2( 0.19984126,  0.78641367), vec2( 0.14383161, -0.14100790)
            );
            ivec2 pp = ivec2(mod(gl_FragCoord.xy, 4.0));
            const float bayer[16] = float[16](
                 0.0/16.0,  8.0/16.0,  2.0/16.0, 10.0/16.0,
                12.0/16.0,  4.0/16.0, 14.0/16.0,  6.0/16.0,
                 3.0/16.0, 11.0/16.0,  1.0/16.0,  9.0/16.0,
                15.0/16.0,  7.0/16.0, 13.0/16.0,  5.0/16.0
            );
            float angle = bayer[pp.y * 4 + pp.x] * 6.28318530718;
            float ca = cos(angle), sa = sin(angle);
            float r = uTexel * 3.0;  // 3-texel radius → smooth 0→1 penumbra gradient

            float vis = 0.0;
            for (int i = 0; i < 16; i++) {
                vec2 s = vec2(disc[i].x * ca - disc[i].y * sa,
                              disc[i].x * sa + disc[i].y * ca) * r;
                vis += texture(tex_shadow, vec3(sc.xy + s, refZ));
            }
            vis *= (1.0 / 16.0);

            fragColor = vec4(vis, 0.0, 1.0, vis);
        }
        """;


    const string AoFragmentSource = """
        #version 450 core
        uniform sampler2D tex_nld;
        uniform sampler2D tex_gnrm;
        uniform vec2 uTanHalf; uniform vec2 uPix;
        uniform float uNear, uFar, uRadius, uStrength;
        uniform vec3 uSunView;
        uniform int uDebugNrm;
        in vec2 vUV; out vec4 fragColor;

        vec3 viewPos(vec2 uv) {
            float d = texture(tex_nld, uv).r;
            float z = d * (uFar - uNear) + uNear;
            return vec3((uv.x * 2.0 - 1.0) * uTanHalf.x * z, (1.0 - uv.y * 2.0) * uTanHalf.y * z, -z);
        }

        float ditherAngle(vec2 fc) {
            ivec2 p = ivec2(mod(fc, 4.0));
            const float dither4x4[16] = float[16](
                 0.0 / 16.0,  8.0 / 16.0,  2.0 / 16.0, 10.0 / 16.0,
                12.0 / 16.0,  4.0 / 16.0, 14.0 / 16.0,  6.0 / 16.0,
                 3.0 / 16.0, 11.0 / 16.0,  1.0 / 16.0,  9.0 / 16.0,
                15.0 / 16.0,  7.0 / 16.0, 13.0 / 16.0,  5.0 / 16.0
            );
            return dither4x4[p.y * 4 + p.x] * 6.28318530718;
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
            if (d >= 0.999) { fragColor = vec4(1.0); return; }
            vec3 p = viewPos(vUV);
            float z = -p.z;
            vec3 n = decodeGBuffNormal(vUV);
            vec2 projRadius = (uRadius / max(0.001, z)) * vec2(1.0 / (2.0 * uTanHalf.x), 1.0 / (2.0 * uTanHalf.y));
            vec2 screenRadius = clamp(projRadius, uPix * 1.5, vec2(0.05));
            float rot = ditherAngle(gl_FragCoord.xy);
            float occ = 0.0;
            const int NUM_PAIRS = 5;
            const int N = NUM_PAIRS * 2;
            float r2 = uRadius * uRadius;
            float bias = 0.12 * uRadius;
            for (int i = 0; i < NUM_PAIRS; ++i) {
                float alpha = (float(i) + 0.5) / float(NUM_PAIRS);
                float angle = alpha * 3.14159265 + rot;
                float r = (alpha * 0.75 + 0.25);
                vec2 dir = vec2(cos(angle), sin(angle)) * r * screenRadius;
                vec2 uv1 = vUV + dir;
                if (uv1.x >= 0.0 && uv1.x <= 1.0 && uv1.y >= 0.0 && uv1.y <= 1.0) {
                    float qd1 = texture(tex_nld, uv1).r;
                    if (qd1 < 0.999) {
                        vec3 q1 = viewPos(uv1);
                        vec3 v1 = q1 - p;
                        float d2_1 = dot(v1, v1);
                        if (d2_1 > 1e-7 && d2_1 < r2) {
                            float vn1 = dot(v1, n) - bias;
                            if (vn1 > 0.0) {
                                float f1 = max(0.0, 1.0 - d2_1 / r2);
                                occ += (f1 * f1 * f1) * (vn1 / (d2_1 + 0.0001));
                            }
                        }
                    }
                }
                vec2 uv2 = vUV - dir;
                if (uv2.x >= 0.0 && uv2.x <= 1.0 && uv2.y >= 0.0 && uv2.y <= 1.0) {
                    float qd2 = texture(tex_nld, uv2).r;
                    if (qd2 < 0.999) {
                        vec3 q2 = viewPos(uv2);
                        vec3 v2 = q2 - p;
                        float d2_2 = dot(v2, v2);
                        if (d2_2 > 1e-7 && d2_2 < r2) {
                            float vn2 = dot(v2, n) - bias;
                            if (vn2 > 0.0) {
                                float f2 = max(0.0, 1.0 - d2_2 / r2);
                                occ += (f2 * f2 * f2) * (vn2 / (d2_2 + 0.0001));
                            }
                        }
                    }
                }
            }
            float ao = clamp(1.0 - (uStrength * 0.6) * occ * (2.0 / float(N)) * uRadius, 0.25, 1.0);
            if (uDebugNrm == 1) { fragColor = vec4(n * 0.5 + 0.5, 1.0); return; }
            fragColor = vec4(1.0, 1.0, 1.0, ao);
        }
        """;

    const string BlurFragmentSource = """
        #version 450 core
        uniform sampler2D tex_ao; uniform sampler2D tex_nld;
        uniform vec2 uStep;
        uniform float uNear, uFar, uRadius;
        in vec2 vUV; out vec4 fragColor;
        void main() {
            float dc = texture(tex_nld, vUV).r;
            if (dc >= 0.999) { fragColor = vec4(1.0); return; }
            vec4 centerSample = texture(tex_ao, vUV);
            float zc = dc * (uFar - uNear) + uNear;
            float sigma_z = max(0.02 * zc, uRadius * 0.5);
            float inv2SigmaZ2 = 0.5 / (sigma_z * sigma_z);
            vec4 acc = vec4(0.0);
            float wsum = 0.0;
            for (int i = -6; i <= 6; ++i) {
                vec2 uv = vUV + uStep * float(i);
                float d = texture(tex_nld, uv).r;
                if (d >= 0.999) continue;
                float z = d * (uFar - uNear) + uNear;
                float dz = z - zc;
                float w = exp(-float(i * i) / 18.0) * exp(-dz * dz * inv2SigmaZ2);
                acc += texture(tex_ao, uv) * w;
                wsum += w;
            }
            fragColor = (wsum > 1e-5) ? (acc / wsum) : centerSample;
        }
        """;

    const string PreshadingFilterFragmentSource = """
        #version 450 core
        uniform sampler2D tex_src;
        uniform vec2 uStep;
        in vec2 vUV; out vec4 fragColor;
        void main() {
            vec4 c0 = texture(tex_src, vUV);
            vec4 cL = texture(tex_src, vUV - uStep);
            vec4 cR = texture(tex_src, vUV + uStep);

            // Nintendo agl_preshading_filter (STEP1/STEP2): 3-tap bilinear Gaussian with weights [6, 5, 6] / 17
            fragColor = c0 * 0.294117659 + (cL + cR) * 0.3529412;
        }
        """;

    const string PreshadingReduceFilterFragmentSource = """
        #version 450 core
        uniform sampler2D tex_src;
        uniform vec2 uStep;
        in vec2 vUV; out vec4 fragColor;
        void main() {
            vec4 c0 = texture(tex_src, vUV - uStep * 0.5);
            vec4 c1 = texture(tex_src, vUV + uStep * 0.5);
            // Nintendo agl_preshading_filter (STEP4/STEP5): 2-tap average
            fragColor = (c0 + c1) * 0.5;
        }
        """;

    const string PreshadingUpsampleFragmentSource = """
        #version 450 core
        uniform sampler2D tex_src;
        in vec2 vUV; out vec4 fragColor;
        void main() {
            fragColor = texture(tex_src, vUV);
        }
        """;


    public ScreenSpaceShadowAndAoPass(GL gl)
    {
        _gl = gl;
        _preshadowProgram = GLProgramBuilder.Build(gl, QuadVertexSource, PreshadowFragmentSource, "preshadow");
        _preshadingFilterProgram = GLProgramBuilder.Build(gl, QuadVertexSource, PreshadingFilterFragmentSource, "preshading_filter");
        _preshadingReduceProgram = GLProgramBuilder.Build(gl, QuadVertexSource, PreshadingReduceFilterFragmentSource, "preshading_reduce");
        _preshadingUpsampleProgram = GLProgramBuilder.Build(gl, QuadVertexSource, PreshadingUpsampleFragmentSource, "preshading_upsample");
        _aoProgram = GLProgramBuilder.Build(gl, QuadVertexSource, AoFragmentSource, "ao");
        _blurProgram = GLProgramBuilder.Build(gl, QuadVertexSource, BlurFragmentSource, "ao_blur");
    }

    public readonly record struct Params(
        Vector4[] ViewInv3Rows, Vector4[] LightViewProj, Vector2 TanHalf, Vector3 SunWorld, Vector3 SunView,
        float Near, float Far, float ShadowBias, float ShadowTexel, float ShadowTexelWorld, float ShadowDepthRange,
        float AoRadius, float AoStrength, uint ShadowTexture,
        CascadeParams? Cascades = null);

    /// <summary>The cascades a frame's shadows come from - see <see cref="FrameRequest.ShadowCascades"/>.</summary>
    /// <param name="ViewProj">Each cascade's light view-projection (row-major 4x4 rows, as <see cref="ShadowPass.LightMatrices.ViewProj"/>).</param>
    /// <param name="TexelWorld">Each cascade's shadow texel size, in metres.</param>
    /// <param name="Bias">Each cascade's depth bias, in its own light-depth units.</param>
    public sealed record CascadeParams(uint Texture, Vector4[][] ViewProj, float[] TexelWorld, float[] Bias);

    public void Run(GLResourceCache resources, RenderTargets targets, Params p)
    {
        _gl.Disable(EnableCap.DepthTest);

        // ---- PreShadow: render initial raw PCF into PreShadow ----
        _gl.UseProgram(_preshadowProgram);
        SetMat4(_preshadowProgram, "uViewInv", Rendering.Mat4Math.ToMat4(p.ViewInv3Rows));
        SetMat4(_preshadowProgram, "uLightViewProj", p.LightViewProj);
        _gl.Uniform2(_gl.GetUniformLocation(_preshadowProgram, "uTanHalf"), p.TanHalf.X, p.TanHalf.Y);
        _gl.Uniform3(_gl.GetUniformLocation(_preshadowProgram, "uSunWorld"), p.SunWorld.X, p.SunWorld.Y, p.SunWorld.Z);
        _gl.Uniform1(_gl.GetUniformLocation(_preshadowProgram, "uNear"), p.Near);
        _gl.Uniform1(_gl.GetUniformLocation(_preshadowProgram, "uFar"), p.Far);
        _gl.Uniform1(_gl.GetUniformLocation(_preshadowProgram, "uBias"), p.ShadowBias);
        _gl.Uniform1(_gl.GetUniformLocation(_preshadowProgram, "uTexel"), p.ShadowTexel);
        _gl.Uniform1(_gl.GetUniformLocation(_preshadowProgram, "uTexelWorld"), p.ShadowTexelWorld);
        _gl.Uniform1(_gl.GetUniformLocation(_preshadowProgram, "uDepthRange"), p.ShadowDepthRange);
        targets.BindColorTarget(targets.PreShadow);
        BindTexture(_preshadowProgram, "tex_nld", 0, targets.LinearDepth.Handle);
        BindTexture(_preshadowProgram, "tex_shadow", 1, p.ShadowTexture);
        BindTexture(_preshadowProgram, "tex_gnrm", 2, targets.GBuffer[3].Handle);
        int count = p.Cascades?.ViewProj.Length ?? 0;
        _gl.Uniform1(_gl.GetUniformLocation(_preshadowProgram, "uCascadeCount"), count);
        _gl.ActiveTexture(TextureUnit.Texture3);
        _gl.BindTexture(TextureTarget.Texture2DArray, p.Cascades?.Texture ?? 0);
        _gl.Uniform1(_gl.GetUniformLocation(_preshadowProgram, "tex_cascades"), 3);
        _gl.ActiveTexture(TextureUnit.Texture0);
        if (p.Cascades is { } cascades)
        {
            _gl.Uniform1(_gl.GetUniformLocation(_preshadowProgram, "uCascadeTexel"), 1f / RenderTargets.CascadeSize);
            for (int c = 0; c < count; c++)
            {
                SetMat4(_preshadowProgram, $"uCascadeViewProj[{c}]", cascades.ViewProj[c]);
                // A wider filter on the finer cascades keeps their penumbra soft; the coarse ones
                // are soft by their texel size alone.
                float radius = c == 0 ? 3f : c == 1 ? 2f : 1.5f;
                _gl.Uniform4(_gl.GetUniformLocation(_preshadowProgram, $"uCascadeParams[{c}]"), cascades.TexelWorld[c], cascades.Bias[c], radius, 0f);
            }
        }
        resources.DrawFullscreenTriangle();

        // Authentic Nintendo 2-tier preshading filter: filters PreShadow into smooth penumbra
        // (PreShadow.rgb gets half-res reduce blur; PreShadow.w gets full-res 3-tap Gaussian via uStack_364 = 7)
        RunPreshadingFilter(resources, targets, targets.PreShadow, targets.AoTmp);

        // ---- AO: raw into AoRaw scratch (reused), then blur into PreMisc ----
        _gl.UseProgram(_aoProgram);
        _gl.Uniform2(_gl.GetUniformLocation(_aoProgram, "uTanHalf"), p.TanHalf.X, p.TanHalf.Y);
        _gl.Uniform2(_gl.GetUniformLocation(_aoProgram, "uPix"), 1f / targets.Width, 1f / targets.Height);
        _gl.Uniform1(_gl.GetUniformLocation(_aoProgram, "uNear"), p.Near);
        _gl.Uniform1(_gl.GetUniformLocation(_aoProgram, "uFar"), p.Far);
        _gl.Uniform1(_gl.GetUniformLocation(_aoProgram, "uRadius"), p.AoRadius);
        _gl.Uniform1(_gl.GetUniformLocation(_aoProgram, "uStrength"), p.AoStrength);
        _gl.Uniform3(_gl.GetUniformLocation(_aoProgram, "uSunView"), p.SunView.X, p.SunView.Y, p.SunView.Z);
        _gl.Uniform1(_gl.GetUniformLocation(_aoProgram, "uDebugNrm"), 0);
        targets.BindColorTarget(targets.AoRaw);
        BindTexture(_aoProgram, "tex_nld", 0, targets.LinearDepth.Handle);
        BindTexture(_aoProgram, "tex_gnrm", 3, targets.GBuffer[3].Handle);
        resources.DrawFullscreenTriangle();
        RunSeparableBlur(resources, targets, targets.AoRaw, targets.AoTmp, targets.PreMisc, p.Near, p.Far, p.AoRadius);
    }

    void RunPreshadingFilter(GLResourceCache resources, RenderTargets targets, GpuTexture preShadow, GpuTexture tmp)
    {
        _gl.UseProgram(_preshadingFilterProgram);

        // Tier 1: Full-resolution anti-aliasing blur (Nintendo agl_preshading_filter STEP1/STEP2)
        // Horizontal into tmp, then vertical directly back into preShadow!
        // Both PreShadow.rgb and PreShadow.w receive the full-resolution anti-aliased 3-tap Gaussian filter!
        targets.BindColorTarget(tmp);
        BindTexture(_preshadingFilterProgram, "tex_src", 0, preShadow.Handle);
        _gl.Uniform2(_gl.GetUniformLocation(_preshadingFilterProgram, "uStep"), 1.3636f / targets.Width, 0f);
        resources.DrawFullscreenTriangle();

        targets.BindColorTarget(preShadow);
        BindTexture(_preshadingFilterProgram, "tex_src", 0, tmp.Handle);
        _gl.Uniform2(_gl.GetUniformLocation(_preshadingFilterProgram, "uStep"), 0f, 1.3636f / targets.Height);
        resources.DrawFullscreenTriangle();

        // Tier 2: Downsample to half-resolution (TotK's Blur0/1(Reduce)) with hardware 2x2 bilinear downsample (STEP3)
        _gl.UseProgram(_preshadingUpsampleProgram);
        targets.BindColorTarget(targets.PreShadowHalf0);
        BindTexture(_preshadingUpsampleProgram, "tex_src", 0, preShadow.Handle);
        resources.DrawFullscreenTriangle();

        // Character blur at half-resolution: 2-tap average (STEP4/STEP5)
        _gl.UseProgram(_preshadingReduceProgram);
        int hw = targets.PreShadowHalf0.Width;
        int hh = targets.PreShadowHalf0.Height;

        targets.BindColorTarget(targets.PreShadowHalf1);
        BindTexture(_preshadingReduceProgram, "tex_src", 0, targets.PreShadowHalf0.Handle);
        _gl.Uniform2(_gl.GetUniformLocation(_preshadingReduceProgram, "uStep"), 1.0f / hw, 0f);
        resources.DrawFullscreenTriangle();

        targets.BindColorTarget(targets.PreShadowHalf0);
        BindTexture(_preshadingReduceProgram, "tex_src", 0, targets.PreShadowHalf1.Handle);
        _gl.Uniform2(_gl.GetUniformLocation(_preshadingReduceProgram, "uStep"), 0f, 1.0f / hh);
        resources.DrawFullscreenTriangle();

        // Upsample back to full-resolution dst (targets.PreShadow) with GL_LINEAR reconstruction (STEP6)
        // ColorMask(true, true, true, false) replicates TotK's uStack_364 = 7:
        // writes half-res blur into PreShadow.rgb, while PreShadow.w keeps
        // the full-resolution anti-aliased 3-tap Gaussian filter from Tier 1!
        _gl.UseProgram(_preshadingUpsampleProgram);
        targets.BindColorTarget(preShadow);
        BindTexture(_preshadingUpsampleProgram, "tex_src", 0, targets.PreShadowHalf0.Handle);
        _gl.ColorMask(true, true, true, false);
        resources.DrawFullscreenTriangle();
        _gl.ColorMask(true, true, true, true);
    }

    void RunSeparableBlur(GLResourceCache resources, RenderTargets targets, GpuTexture src, GpuTexture tmp, GpuTexture dst, float near, float far, float radius)
    {
        _gl.UseProgram(_blurProgram);
        _gl.Uniform1(_gl.GetUniformLocation(_blurProgram, "uNear"), near);
        _gl.Uniform1(_gl.GetUniformLocation(_blurProgram, "uFar"), far);
        _gl.Uniform1(_gl.GetUniformLocation(_blurProgram, "uRadius"), radius);

        (GpuTexture Src, GpuTexture Dst, Vector2 Step)[] steps =
        [
            (src, tmp, new Vector2(1f / targets.Width, 0f)),
            (tmp, dst, new Vector2(0f, 1f / targets.Height)),
        ];
        foreach (var (s, d, step) in steps)
        {
            targets.BindColorTarget(d);
            BindTexture(_blurProgram, "tex_ao", 0, s.Handle);
            BindTexture(_blurProgram, "tex_nld", 1, targets.LinearDepth.Handle);
            _gl.Uniform2(_gl.GetUniformLocation(_blurProgram, "uStep"), step.X, step.Y);
            resources.DrawFullscreenTriangle();
        }
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

    public void Dispose()
    {
        _gl.DeleteProgram(_preshadowProgram);
        _gl.DeleteProgram(_preshadingFilterProgram);
        _gl.DeleteProgram(_preshadingReduceProgram);
        _gl.DeleteProgram(_preshadingUpsampleProgram);
        _gl.DeleteProgram(_aoProgram);
        _gl.DeleteProgram(_blurProgram);
    }
}
