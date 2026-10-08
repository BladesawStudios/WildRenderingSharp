using System.Numerics;
using Silk.NET.OpenGL;

namespace WildRenderingSharp.Pipeline;

/// <summary>A simplified single-pass FXAA-style edge-aware blur, the anti-aliasing used instead of supersampling by default.</summary>
public sealed class FxaaPass : IDisposable
{
    readonly GL _gl;
    readonly uint _program;

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

            // Flat or low-contrast area: leave it alone. The threshold is below textbook FXAA on purpose, since the artifact this hides (an isolated single-texel dither dot on a thin bright strand) is a small sharp feature a stricter threshold would skip.
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

            // Subpixel term: how far the centre sits from a low-pass average of its 3x3 neighbourhood, over the local contrast range. It catches an isolated dither dot, which has no long edge for the directional search to follow.
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

            // Edge-directional blend, sampling a full texel toward the higher-contrast neighbour so it crosses onto the other side of the edge instead of re-blending the same two texels.
            float edgeBlend = clamp(lumaRange / max(lumaMax, 1e-4), 0.0, 1.0);
            vec3 edgeBlended = texture(t, vUV + blendDir).rgb;

            // Combine both terms: a dither dot benefits mostly from the subpixel term, a polygon edge from the directional one, and many pixels are a mix.
            float blendFactor = clamp(max(subpixel, edgeBlend * 0.75), 0.0, 1.0);
            vec3 blended = mix(edgeBlended, boxAvg3, subpixel);
            // Alpha passes through unblended: PresentPass already wrote the real coverage alpha (Background: Transparent) into this source, and it is not what this pass smooths.
            fragColor = vec4(mix(centerColor, blended, blendFactor), texture(t, vUV).a);
        }
        """;

    public FxaaPass(GL gl)
    {
        _gl = gl;
        _program = GLProgramBuilder.Build(gl, FullscreenShaders.Vertex450, FragmentSource, "fxaa");
    }

    public void Run(GLResourceCache resources, GpuTexture source)
    {
        _gl.UseProgram(_program);
        _gl.BindTextureUniform(_program, "t", 0, source.Handle);
        _gl.SetVec2(_program, "uTexel", new Vector2(1f / source.Width, 1f / source.Height));
        resources.DrawFullscreenTriangle();
    }

    public void Dispose() => _gl.DeleteProgram(_program);
}
