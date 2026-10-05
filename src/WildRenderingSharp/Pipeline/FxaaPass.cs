using System.Numerics;
using Silk.NET.OpenGL;

namespace WildRenderingSharp.Pipeline;

/// <summary>
/// A simplified, single-pass, FXAA-style edge-aware blur - the replacement for supersampling as
/// the viewport's anti-aliasing method.
///
/// WHY: supersampling (rendering at 2x resolution then box-downsampling in <see cref="PresentPass"/>)
/// doubled the actual fragment-shading resolution, which changes the density of any real, per-pixel
/// dither pattern the compiled game shaders evaluate against <c>gl_FragCoord</c> - hair rendering's
/// screen-door-style edge dither (a real technique for faking soft strand edges without true alpha
/// blending) looks correct at native resolution but aliases into visible, evenly-spaced dark bands
/// once the shading resolution no longer matches what the dither pattern's period assumes, then
/// SURVIVES the box-downsample instead of averaging out (confirmed: reported as sharp, evenly-spaced
/// bands specifically at 2x quality, gone at 1x). Post-process AA sidesteps this entirely by keeping
/// the fragment-shading resolution native - the dither pattern stays exactly as the real shader
/// intends, and this pass only smooths the FINAL, already-correct image's aliased edges.
///
/// This is a compact approximation of NVIDIA's public FXAA technique (luma-based edge detection,
/// single directional blend toward the higher-contrast neighbour) rather than a full multi-tap
/// sub-pixel search - sufficient here because the artifact being targeted is isolated, high-contrast
/// single-texel dither cells along thin bright strands, not general polygon-edge aliasing.
/// </summary>
public sealed class FxaaPass : IDisposable
{
    readonly GL _gl;
    readonly uint _program;

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

    const string FragmentSource = """
        #version 450 core
        uniform sampler2D t;
        uniform vec2 uTexel;
        in vec2 vUV;
        out vec4 fragColor;

        float luma(vec3 c) { return dot(c, vec3(0.299, 0.587, 0.114)); }

        void main() {
            vec3 centerColor = texture(t, vUV).rgb;
            float lumaCenter = luma(centerColor);

            vec3 upColor    = texture(t, vUV + vec2(0.0, uTexel.y)).rgb;
            vec3 downColor  = texture(t, vUV - vec2(0.0, uTexel.y)).rgb;
            vec3 leftColor  = texture(t, vUV - vec2(uTexel.x, 0.0)).rgb;
            vec3 rightColor = texture(t, vUV + vec2(uTexel.x, 0.0)).rgb;
            float lumaUp = luma(upColor), lumaDown = luma(downColor), lumaLeft = luma(leftColor), lumaRight = luma(rightColor);

            float lumaMin = min(lumaCenter, min(min(lumaUp, lumaDown), min(lumaLeft, lumaRight)));
            float lumaMax = max(lumaCenter, max(max(lumaUp, lumaDown), max(lumaLeft, lumaRight)));
            float lumaRange = lumaMax - lumaMin;

            // Flat/low-contrast area - leave it alone rather than waste a blend on nothing. Lower
            // than a "textbook" FXAA threshold on purpose: the artifact this exists to hide (an
            // isolated single-texel dither dot on a thin bright strand) is exactly the kind of
            // small, sharp feature a stricter threshold would skip as "not really an edge."
            if (lumaRange < max(0.0625, lumaMax * 0.0625)) {
                fragColor = vec4(centerColor, texture(t, vUV).a);
                return;
            }

            vec3 upLeftColor    = texture(t, vUV + vec2(-uTexel.x,  uTexel.y)).rgb;
            vec3 upRightColor   = texture(t, vUV + vec2( uTexel.x,  uTexel.y)).rgb;
            vec3 downLeftColor  = texture(t, vUV + vec2(-uTexel.x, -uTexel.y)).rgb;
            vec3 downRightColor = texture(t, vUV + vec2( uTexel.x, -uTexel.y)).rgb;
            float lumaUpLeft = luma(upLeftColor), lumaUpRight = luma(upRightColor);
            float lumaDownLeft = luma(downLeftColor), lumaDownRight = luma(downRightColor);

            // Subpixel-aliasing term: how far the centre sits from a low-pass average of its whole
            // 3x3 neighbourhood, normalised by the local contrast range. This is what actually
            // catches an isolated dither dot - a single stray texel has no long "edge" for the
            // directional search below to follow, but it stands out hugely against a smoothed
            // average of everything around it.
            vec3 boxAvg3 = (upColor + downColor + leftColor + rightColor
                           + upLeftColor + upRightColor + downLeftColor + downRightColor + centerColor) / 9.0;
            float subpixel = clamp(abs(luma(boxAvg3) - lumaCenter) / max(lumaRange, 1e-4), 0.0, 1.0);
            subpixel = subpixel * subpixel * (3.0 - 2.0 * subpixel); // smoothstep-shape the ramp

            float edgeHorizontal = abs(lumaUpLeft + lumaUpRight - 2.0 * lumaUp) * 1.0
                                  + abs(lumaLeft + lumaRight - 2.0 * lumaCenter) * 2.0
                                  + abs(lumaDownLeft + lumaDownRight - 2.0 * lumaDown);
            float edgeVertical   = abs(lumaUpLeft + lumaDownLeft - 2.0 * lumaLeft) * 1.0
                                  + abs(lumaUp + lumaDown - 2.0 * lumaCenter) * 2.0
                                  + abs(lumaUpRight + lumaDownRight - 2.0 * lumaRight);
            bool isHorizontal = edgeHorizontal >= edgeVertical;

            float gradPositive = abs((isHorizontal ? lumaDown : lumaRight) - lumaCenter);
            float gradNegative = abs((isHorizontal ? lumaUp : lumaLeft) - lumaCenter);
            bool isPositive = gradPositive >= gradNegative;
            float stepLength = isHorizontal ? uTexel.y : uTexel.x;

            vec2 blendDir = isHorizontal ? vec2(0.0, isPositive ? stepLength : -stepLength)
                                          : vec2(isPositive ? stepLength : -stepLength, 0.0);

            // Edge-directional blend, sampling a FULL texel toward the higher-contrast neighbour
            // (not a half-texel tap) so it actually crosses onto the other side of the edge instead
            // of re-blending mostly the same two texels the min/max already came from.
            float edgeBlend = clamp(lumaRange / max(lumaMax, 1e-4), 0.0, 1.0);
            vec3 edgeBlended = texture(t, vUV + blendDir).rgb;

            // Combine both terms rather than pick one - a dither dot benefits mostly from the
            // subpixel term, a real polygon edge mostly from the directional term, and many pixels
            // are some mix of both.
            float blendFactor = clamp(max(subpixel, edgeBlend * 0.75), 0.0, 1.0);
            vec3 blended = mix(edgeBlended, boxAvg3, subpixel);
            // Alpha passes straight through unblended - PresentPass already wrote the real
            // coverage alpha (Background: Transparent) into this same source texture by the time
            // FXAA runs, and alpha was never part of what this pass is smoothing.
            fragColor = vec4(mix(centerColor, blended, blendFactor), texture(t, vUV).a);
        }
        """;

    public FxaaPass(GL gl)
    {
        _gl = gl;
        _program = GLProgramBuilder.Build(gl, QuadVertexSource, FragmentSource, "fxaa");
    }

    /// <summary>Caller must have already bound the destination framebuffer/viewport - mirrors <see cref="PresentPass"/>'s own convention.</summary>
    public void Run(GLResourceCache resources, GpuTexture source)
    {
        _gl.UseProgram(_program);
        _gl.BindTextureUniform(_program, "t", 0, source.Handle);
        _gl.SetVec2(_program, "uTexel", new Vector2(1f / source.Width, 1f / source.Height));
        resources.DrawFullscreenTriangle();
    }

    public void Dispose() => _gl.DeleteProgram(_program);
}
