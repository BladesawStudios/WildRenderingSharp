using WildRenderingSharp.Rendering;
using Silk.NET.OpenGL;

namespace WildRenderingSharp.Pipeline;

/// <summary>
/// The game's final grade - <c>agl::pfx::ColorCorrection</c>, driven by the real
/// <c>postfx/master_field.baglccr</c> - applied after <c>agl_hdr_compose</c>.
/// </summary>
/// <remarks>
/// <para>
/// WildRenderingSharp never applied this at all, which matters for every visual comparison made against the
/// game: a screenshot is a GRADED image and WildRenderingSharp's output was not. The single most visible field
/// is <c>saturation = 1.175</c>, so the shipped game is ~17.5% more saturated than its own raw
/// render - enough on its own to make a correct render look washed out beside a screenshot.
/// </para>
/// <para>
/// Written by hand rather than decompiled: unlike the sky and cloud programs, colour correction is
/// simple, well-understood arithmetic (HSB, gamma, lift/gain) and its PARAMETERS are what carry the
/// game's look, not its instruction sequence. The values are the real authored ones.
/// </para>
/// <para>
/// The <c>level</c> curve array is not implemented. AAMP curves need a real interpolator, and a
/// guessed curve would silently reshape the whole image - see <see cref="ColorCorrectionPostFx"/>.
/// </para>
/// </remarks>
public sealed class ColorCorrectionPass : IDisposable
{
    readonly GL _gl;
    readonly uint _program;
    readonly uint _blitProgram;

    // Its OWN scratch, deliberately not targets.Scene. A texture cannot be its own sampler and
    // render target in one pass, so a grade in place needs a bounce buffer - but borrowing the
    // forward pass's Scene buffer means inheriting THAT pass's orientation convention (Scene exists
    // to hold a Y-FLIPPED copy for depth-testing against the G-buffer), and copying back through a
    // helper with its own flip semantics produced a vertically INVERTED frame. Owning the scratch
    // and blitting with the very same vertex shader in both directions makes the orientation
    // trivially self-consistent instead of a question about someone else's convention.
    uint _scratchTex, _scratchFbo;
    int _scratchW, _scratchH;

    const string VertexSource = """
        #version 330 core
        out vec2 vUV;
        void main()
        {
            vUV = vec2(float((gl_VertexID << 1) & 2), float(gl_VertexID & 2));
            gl_Position = vec4(vUV * 2.0 - 1.0, 0.0, 1.0);
        }
        """;

    // Rec.709 luminance - the same weighting the rest of this pipeline uses for perceived
    // brightness, so saturation pivots around a colour's real luminance rather than a flat average.
    const string BlitFragmentSource = """
        #version 330 core
        in vec2 vUV;
        uniform sampler2D tSrc;
        out vec4 oCol;
        void main() { oCol = texture(tSrc, vUV); }
        """;

    const string FragmentSource = """
        #version 330 core
        in vec2 vUV;
        uniform sampler2D tScene;
        uniform float uHue, uSaturation, uBrightness, uGamma;
        uniform int uToycamEnable, uOrderToycamHsb;
        uniform vec3 uOffset1, uOffset2, uLevel1, uLevel2, uMulColor;
        uniform float uSat1, uSat2, uToyBrightness, uToyContrast;
        out vec4 oCol;

        const vec3 LUMA = vec3(0.2126, 0.7152, 0.0722);

        vec3 applySaturation(vec3 c, float s)
        {
            return mix(vec3(dot(c, LUMA)), c, s);
        }

        vec3 applyHue(vec3 c, float radians)
        {
            if (abs(radians) < 1e-5) return c;
            // Rotation about the luma axis in RGB - equivalent to an HSV hue shift but without the
            // conversion's discontinuity at the hue wrap, which would band a smooth sky gradient.
            float cosA = cos(radians), sinA = sin(radians);
            float k = 1.0 / 3.0, sq = sqrt(k);
            mat3 m = mat3(
                cosA + (1.0 - cosA) * k,        k * (1.0 - cosA) - sq * sinA, k * (1.0 - cosA) + sq * sinA,
                k * (1.0 - cosA) + sq * sinA,   cosA + k * (1.0 - cosA),      k * (1.0 - cosA) - sq * sinA,
                k * (1.0 - cosA) - sq * sinA,   k * (1.0 - cosA) + sq * sinA, cosA + k * (1.0 - cosA));
            return m * c;
        }

        vec3 applyToycam(vec3 c)
        {
            // Two lift/gain stages with their own saturation, then contrast about mid grey and a
            // colour multiply - the shape agl's own toycam parameters describe.
            c = applySaturation(c * uLevel1 + uOffset1, uSat1);
            c = applySaturation(c * uLevel2 + uOffset2, uSat2);
            c = (c - 0.5) * uToyContrast + 0.5;
            return c * uToyBrightness * uMulColor;
        }

        void main()
        {
            vec3 c = texture(tScene, vUV).rgb;

            if (uToycamEnable == 1 && uOrderToycamHsb == 1)
                c = applyToycam(c);

            c = applyHue(c, uHue);
            c = applySaturation(c, uSaturation);
            c *= uBrightness;

            if (uToycamEnable == 1 && uOrderToycamHsb == 0)
                c = applyToycam(c);

            // Gamma last, and guarded: this runs on a tonemapped image, but a negative value can
            // still reach here from a saturation boost pushing a channel below zero, and pow() of a
            // negative is undefined - it shows up as black speckle rather than an error.
            if (abs(uGamma - 1.0) > 1e-5)
                c = pow(max(c, vec3(0.0)), vec3(1.0 / max(uGamma, 1e-4)));

            oCol = vec4(c, 1.0);
        }
        """;

    public ColorCorrectionPass(GL gl)
    {
        _gl = gl;
        _program = GLProgramBuilder.Build(gl, VertexSource, FragmentSource, "color_correction");
        _blitProgram = GLProgramBuilder.Build(gl, VertexSource, BlitFragmentSource, "color_correction_blit");
    }

    /// <summary>Grades <paramref name="target"/> in place. Returns false if the grade is a no-op.</summary>
    public unsafe bool Run(GLResourceCache resources, RenderTargets targets, GpuTexture target,
        ColorCorrectionPostFx cc)
    {
        if (cc.IsIdentity)
            return false;

        EnsureScratch(target.Width, target.Height);

        // Pass 1: graded copy into our own scratch.
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _scratchFbo);
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, _scratchTex, 0);
        _gl.Viewport(0, 0, (uint)_scratchW, (uint)_scratchH);
        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.Blend);
        _gl.UseProgram(_program);
        _gl.BindTextureUniform(_program, "tScene", 0, target.Handle);
        _gl.SetFloat(_program, "uHue", cc.Hue);
        _gl.SetFloat(_program, "uSaturation", cc.Saturation);
        _gl.SetFloat(_program, "uBrightness", cc.Brightness);
        _gl.SetFloat(_program, "uGamma", cc.Gamma);
        _gl.SetInt(_program, "uToycamEnable", cc.ToycamEnable ? 1 : 0);
        _gl.SetInt(_program, "uOrderToycamHsb", cc.OrderToycamHsb ? 1 : 0);
        _gl.SetVec3(_program, "uOffset1", cc.ToycamOffset1);
        _gl.SetVec3(_program, "uOffset2", cc.ToycamOffset2);
        _gl.SetVec3(_program, "uLevel1", cc.ToycamLevel1);
        _gl.SetVec3(_program, "uLevel2", cc.ToycamLevel2);
        _gl.SetVec3(_program, "uMulColor", cc.ToycamMulColor);
        _gl.SetFloat(_program, "uSat1", cc.ToycamSaturation1);
        _gl.SetFloat(_program, "uSat2", cc.ToycamSaturation2);
        _gl.SetFloat(_program, "uToyBrightness", cc.ToycamBrightness);
        _gl.SetFloat(_program, "uToyContrast", cc.ToycamContrast);
        resources.DrawFullscreenTriangle();

        // Pass 2: straight back, through the SAME vertex shader - so whatever orientation pass 1
        // wrote, pass 2 reads identically and the round trip cannot flip.
        targets.BindColorTarget(target);
        _gl.UseProgram(_blitProgram);
        _gl.BindTextureUniform(_blitProgram, "tSrc", 0, _scratchTex);
        resources.DrawFullscreenTriangle();
        return true;
    }

    unsafe void EnsureScratch(int width, int height)
    {
        if (width == _scratchW && height == _scratchH && _scratchTex != 0)
            return;
        if (_scratchTex != 0) _gl.DeleteTexture(_scratchTex);
        if (_scratchFbo == 0) _scratchFbo = _gl.GenFramebuffer();
        _scratchW = Math.Max(1, width);
        _scratchH = Math.Max(1, height);
        _scratchTex = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, _scratchTex);
        // Matches Ldr's own Rgba32f: the grade runs on tonemapped but still float data, and a
        // narrower scratch would quantise it on the way through.
        _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba32f, (uint)_scratchW, (uint)_scratchH, 0,
            PixelFormat.Rgba, PixelType.Float, null);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
    }

    public void Dispose()
    {
        if (_program != 0) _gl.DeleteProgram(_program);
        if (_blitProgram != 0) _gl.DeleteProgram(_blitProgram);
        if (_scratchTex != 0) _gl.DeleteTexture(_scratchTex);
        if (_scratchFbo != 0) _gl.DeleteFramebuffer(_scratchFbo);
    }
}
