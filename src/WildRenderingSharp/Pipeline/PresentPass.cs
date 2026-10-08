using System.Numerics;
using Silk.NET.OpenGL;

namespace WildRenderingSharp.Pipeline;

/// <summary>
/// The final blit: box-downsamples the supersampled render in linear light (filtering after the sRGB encode would darken edges),
/// applies <c>agl</c>'s colour-correction curve (hue, saturation, brightness, gamma) and sRGB-encodes. Also offers a Reinhard HDR
/// preview for inspecting <c>rt_final</c>. The caller must have bound the destination framebuffer and viewport; this pass knows
/// only its source texture and the sampling and grading maths.
/// </summary>
public sealed class PresentPass : IDisposable
{
    readonly GL _gl;
    readonly uint _program;

    const string FragmentSource = """
        #version 450 core
        uniform sampler2D t; uniform int uMode; uniform int uSS; uniform vec2 uTexel;
        uniform float uSaturation; uniform float uBrightness; uniform float uGamma; uniform float uRawScale;
        uniform sampler2D tAlpha; uniform int uUseAlpha;
        in vec2 vUV; out vec4 f;
        void main() {
            // Mode 2: a diagnostic passthrough for near-zero buffers (the pass-ID mask, one PreShadow or PreMisc channel): no supersample averaging or grading, just scaled so small values are
            // distinguishable. Also the plain "no AA" blit of an already-graded image, where the source's alpha must survive (Background: Transparent; see alphaSource).
            if (uMode == 2) {
                float a2 = uUseAlpha == 1 ? texture(tAlpha, vUV).a : 1.0;
                f = vec4(texture(t, vUV).rgb * uRawScale, a2);
                return;
            }

            vec3 c = vec3(0.0);
            float alphaSum = 0.0;
            float valid_samples = 0.0;
            for (int y = 0; y < uSS; ++y) {
                for (int x = 0; x < uSS; ++x) {
                    vec2 off = (vec2(x, y) - 0.5 * float(uSS - 1)) * uTexel;
                    vec3 s = texture(t, vUV + off).rgb;
                    if (!isnan(s.r) && !isnan(s.g) && !isnan(s.b) && !isinf(s.r) && !isinf(s.g) && !isinf(s.b)) {
                        c += s;
                        valid_samples += 1.0;
                    }
                    if (uUseAlpha == 1) alphaSum += texture(tAlpha, vUV + off).a;
                }
            }
            if (valid_samples > 0.0) c /= valid_samples;
            if (uMode == 1) c = max(c, 0.0) / (1.0 + max(c, 0.0));

            float luma = dot(c, vec3(0.2989, 0.5866, 0.1144));
            c = luma + (c - luma) * uSaturation;
            c = clamp(c * uBrightness, 0.0, 1.0);
            if (uGamma != 1.0) c = pow(c, vec3(1.0 / uGamma));

            c = clamp(c, 0.0, 1.0);
            float outAlpha = uUseAlpha == 1 ? clamp(alphaSum / float(uSS * uSS), 0.0, 1.0) : 1.0;
            f = vec4(mix(c * 12.92, 1.055 * pow(c, vec3(1.0 / 2.4)) - 0.055, step(0.0031308, c)), outAlpha);
        }
        """;

    public PresentPass(GL gl)
    {
        _gl = gl;
        _program = GLProgramBuilder.Build(gl, FullscreenShaders.Vertex450, FragmentSource, "present_blit");
    }

    /// <summary>
    /// <paramref name="hdrPreview"/> switches to the Reinhard-tonemapped raw-HDR view; normally false, presenting the tonemapped
    /// LDR result.
    /// </summary>
    public void Run(GLResourceCache resources, GpuTexture source, int supersample, float saturation, float brightness, float gamma, bool hdrPreview = false, GpuTexture? alphaSource = null)
    {
        _gl.UseProgram(_program);
        _gl.BindTextureUniform(_program, "t", 0, source.Handle);
        _gl.SetInt(_program, "uMode", hdrPreview ? 1 : 0);
        _gl.SetInt(_program, "uSS", Math.Max(1, supersample));
        _gl.SetVec2(_program, "uTexel", new Vector2(1f / source.Width, 1f / source.Height));
        _gl.SetFloat(_program, "uSaturation", saturation);
        _gl.SetFloat(_program, "uBrightness", brightness);
        _gl.SetFloat(_program, "uGamma", gamma);
        BindAlpha(alphaSource);
        resources.DrawFullscreenTriangle();
    }

    /// <summary>
    /// Diagnostic passthrough (no supersampling, no grading) with a brightness multiply, for inspecting a near-zero buffer like the
    /// pass-ID mask or, with <paramref name="alphaSource"/>, as the plain "AA off" blit that preserves Background: Transparent's
    /// alpha (see <see cref="Run"/>).
    /// </summary>
    public void RunRaw(GLResourceCache resources, GpuTexture source, float scale, GpuTexture? alphaSource = null)
    {
        _gl.UseProgram(_program);
        _gl.BindTextureUniform(_program, "t", 0, source.Handle);
        _gl.SetInt(_program, "uMode", 2);
        _gl.SetFloat(_program, "uRawScale", scale);
        BindAlpha(alphaSource);
        resources.DrawFullscreenTriangle();
    }

    // Binds a real alpha source (see Run) at a unit distinct from t. Always bound to something valid, falling back to unit 0's
    // texture, so tAlpha never points at an incomplete texture whatever uUseAlpha says.
    void BindAlpha(GpuTexture? alphaSource)
    {
        _gl.BindTextureUniform(_program, "tAlpha", 1, (alphaSource ?? default).Handle);
        _gl.SetInt(_program, "uUseAlpha", alphaSource is null ? 0 : 1);
    }

    public void Dispose() => _gl.DeleteProgram(_program);
}
