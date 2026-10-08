using Silk.NET.OpenGL;

namespace WildRenderingSharp.Pipeline;

/// <summary>
/// Builds <c>cTex_NormalizedLinearDepth</c> - <c>(viewZ - near) / (far - near)</c> - at full
/// resolution from the G-buffer's hardware depth, then a half-resolution copy via a 4-tap
/// <c>textureGather</c> min (matching <c>prog_nld</c>/<c>prog_nld_half</c>). Every consumer that
/// reconstructs a view/world position (the shadow projection, the AO, the deferred resolve's own
/// specular) reads this rather than the raw depth buffer, because the deferred passes decode it
/// with <c>fma(sample, cCameraParam2.x, cCameraParam0.x)</c>, i.e. exactly this formula inverted.
/// </summary>
public sealed class LinearDepthPass : IDisposable
{
    readonly GL _gl;
    readonly uint _fullProgram, _halfProgram;

    const string FullFragmentSource = """
        #version 450 core
        uniform sampler2D tex_depth;
        uniform float uNear; uniform float uFar;
        in vec2 vUV; out vec4 fragColor;
        void main() {
            float d = texture(tex_depth, vUV).r;
            float ndc = d * 2.0 - 1.0;
            float viewZ = (2.0 * uNear * uFar) / (uFar + uNear - ndc * (uFar - uNear));
            fragColor = vec4(clamp((viewZ - uNear) / (uFar - uNear), 0.0, 1.0));
        }
        """;

    const string HalfFragmentSource = """
        #version 450 core
        uniform sampler2D tex_nld;
        in vec2 vUV; out vec4 fragColor;
        void main() {
            vec4 s = textureGather(tex_nld, vUV, 0);
            fragColor = vec4(min(min(s.x, s.y), min(s.z, s.w)));
        }
        """;

    public LinearDepthPass(GL gl)
    {
        _gl = gl;
        _fullProgram = GLProgramBuilder.Build(gl, FullscreenShaders.Vertex450, FullFragmentSource, "linear_depth_full");
        _halfProgram = GLProgramBuilder.Build(gl, FullscreenShaders.Vertex450, HalfFragmentSource, "linear_depth_half");
    }

    public void Run(GLResourceCache resources, RenderTargets targets, float near, float far)
    {
        _gl.Disable(EnableCap.DepthTest);

        _gl.UseProgram(_fullProgram);
        targets.BindColorTarget(targets.LinearDepth);
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, targets.GBufferDepth.Handle);
        _gl.Uniform1(_gl.GetUniformLocation(_fullProgram, "tex_depth"), 0);
        _gl.Uniform1(_gl.GetUniformLocation(_fullProgram, "uNear"), near);
        _gl.Uniform1(_gl.GetUniformLocation(_fullProgram, "uFar"), far);
        resources.DrawFullscreenTriangle();

        _gl.UseProgram(_halfProgram);
        targets.BindColorTarget(targets.LinearDepthHalf);
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, targets.LinearDepth.Handle);
        _gl.Uniform1(_gl.GetUniformLocation(_halfProgram, "tex_nld"), 0);
        resources.DrawFullscreenTriangle();
    }

    public void Dispose()
    {
        _gl.DeleteProgram(_fullProgram);
        _gl.DeleteProgram(_halfProgram);
    }
}
