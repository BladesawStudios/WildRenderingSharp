using System.Numerics;
using Silk.NET.OpenGL;

namespace WildRenderingSharp.Pipeline;

/// <summary>
/// Bright-pass -> 4 downsampled+blurred levels -> weighted compose, feeding straight into <c>agl_hdr_compose</c>'s own
/// <c>cBloom</c> sampler (so it lands in the game's own compose, not a separate effect bolted on afterwards). Mirrors
/// <c>BLOOM_BRIGHT_SRC</c>/ <c>BLOOM_BLUR_SRC</c>/<c>BLOOM_COMPOSE_SRC</c> and the bloom loop in <c>render_scene</c> exactly,
/// including its slightly asymmetric level 0 (bright-pass only) vs. levels 1-3 (one more blur-and-downsample step before the usual
/// two-pass separable blur).
/// </summary>
public sealed class BloomPass : IDisposable
{
    readonly GL _gl;
    readonly uint _brightProgram, _blurProgram, _composeProgram;

    /// <summary>Per-blur-level tint (.rgb) and weight (.a). Levels 2/3 carry the warm tint that gives TotK's bloom its orange cast.</summary>
    public static readonly Vector4[] LevelColors =
    [
        new(1f, 1f, 1f, 0.50f),
        new(1f, 1f, 1f, 1.00f),
        new(1f, 0.48f, 0.30f, 1.00f),
        new(1f, 0.48f, 0.30f, 0.00f),
    ];
    public static readonly Vector3 ComposeColor = new(1.0f, 0.935f, 0.833f);
    public static readonly Vector3 Balance = Vector3.One;

    const string BrightFragmentSource = """
        #version 450 core
        uniform sampler2D t; uniform vec3 uBalance; uniform float uThreshold, uClamp;
        in vec2 vUV; out vec4 fragColor;
        void main() {
            vec3 c = texture(t, vUV).rgb * uBalance;
            c = max(c - uThreshold, 0.0);
            fragColor = vec4(min(c, uClamp), 1.0);
        }
        """;

    const string BlurFragmentSource = """
        #version 450 core
        uniform sampler2D t; uniform vec2 uStep; in vec2 vUV; out vec4 fragColor;
        void main() {
            float w[5] = float[](0.2270270, 0.1945946, 0.1216216, 0.0540541, 0.0162162);
            vec3 acc = texture(t, vUV).rgb * w[0];
            for (int i = 1; i < 5; ++i) {
                acc += texture(t, vUV + uStep * float(i)).rgb * w[i];
                acc += texture(t, vUV - uStep * float(i)).rgb * w[i];
            }
            fragColor = vec4(acc, 1.0);
        }
        """;

    const string ComposeFragmentSource = """
        #version 450 core
        uniform sampler2D l0; uniform sampler2D l1; uniform sampler2D l2; uniform sampler2D l3;
        uniform vec4 c0; uniform vec4 c1; uniform vec4 c2; uniform vec4 c3;
        uniform vec3 uCompose; uniform float uIntensity;
        in vec2 vUV; out vec4 fragColor;
        void main() {
            vec3 b = texture(l0, vUV).rgb * c0.rgb * c0.a
                   + texture(l1, vUV).rgb * c1.rgb * c1.a
                   + texture(l2, vUV).rgb * c2.rgb * c2.a
                   + texture(l3, vUV).rgb * c3.rgb * c3.a;
            fragColor = vec4(b * uCompose * uIntensity, 1.0);
        }
        """;

    public BloomPass(GL gl)
    {
        _gl = gl;
        _brightProgram = GLProgramBuilder.Build(gl, FullscreenShaders.Vertex450, BrightFragmentSource, "bloom_bright");
        _blurProgram = GLProgramBuilder.Build(gl, FullscreenShaders.Vertex450, BlurFragmentSource, "bloom_blur");
        _composeProgram = GLProgramBuilder.Build(gl, FullscreenShaders.Vertex450, ComposeFragmentSource, "bloom_compose");
    }

    public void Run(GLResourceCache resources, RenderTargets targets, GpuTexture hdrSource, float threshold, float clamp, float intensity)
    {
        _gl.Disable(EnableCap.DepthTest);

        var cur = hdrSource;
        for (int i = 0; i < RenderTargets.BloomLevelCount; i++)
        {
            var lv = targets.BloomLevels[i];
            var tmp = targets.BloomTmp[i];

            targets.BindColorTarget(lv);
            if (i == 0)
            {
                _gl.UseProgram(_brightProgram);
                _gl.SetVec3(_brightProgram, "uBalance", Balance);
                _gl.SetFloat(_brightProgram, "uThreshold", threshold);
                _gl.SetFloat(_brightProgram, "uClamp", clamp);
                _gl.BindTextureUniform(_brightProgram, "t", 0, cur.Handle);
            }
            else
            {
                _gl.UseProgram(_blurProgram);
                _gl.BindTextureUniform(_blurProgram, "t", 0, cur.Handle);
                _gl.SetVec2(_blurProgram, "uStep", new Vector2(1f / lv.Width, 0f));
            }
            resources.DrawFullscreenTriangle();

            _gl.UseProgram(_blurProgram);
            foreach (var (src, dst, step) in new[]
                     {
                         (lv, tmp, new Vector2(1f / lv.Width, 0f)),
                         (tmp, lv, new Vector2(0f, 1f / lv.Height)),
                     })
            {
                targets.BindColorTarget(dst);
                _gl.BindTextureUniform(_blurProgram, "t", 0, src.Handle);
                _gl.SetVec2(_blurProgram, "uStep", step);
                resources.DrawFullscreenTriangle();
            }
            cur = lv;
        }

        targets.BindColorTarget(targets.Bloom);
        _gl.UseProgram(_composeProgram);
        for (int i = 0; i < RenderTargets.BloomLevelCount; i++)
        {
            _gl.BindTextureUniform(_composeProgram, $"l{i}", i, targets.BloomLevels[i].Handle);
            _gl.SetVec4(_composeProgram, $"c{i}", LevelColors[i]);
        }
        _gl.SetVec3(_composeProgram, "uCompose", ComposeColor);
        _gl.SetFloat(_composeProgram, "uIntensity", intensity);
        resources.DrawFullscreenTriangle();
    }

    public void Dispose()
    {
        _gl.DeleteProgram(_brightProgram);
        _gl.DeleteProgram(_blurProgram);
        _gl.DeleteProgram(_composeProgram);
    }
}
