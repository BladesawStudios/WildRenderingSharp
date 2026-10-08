using WildRenderingSharp.Graphics;
using Silk.NET.OpenGL;
using WildRenderingSharp.Profiles.Totk.Atmosphere;
using WildRenderingSharp.Pipeline;

namespace WildRenderingSharp.Profiles.Totk.PostProcess;

/// <summary>
/// The game's final grade, <c>agl::pfx::ColorCorrection</c> driven by <c>postfx/master_field.baglccr</c>, applied after
/// <c>agl_hdr_compose</c>.
/// </summary>
public sealed class ColorCorrectionPass : IDisposable
{
    readonly GL _gl;
    readonly uint _program;
    readonly uint _blitProgram;

    // Its own scratch, not targets.Scene: grading in place needs a bounce buffer, but Scene holds a Y-flipped copy for depth-testing against the G-buffer, and copying back through a helper with its own
    // flip semantics produced an inverted frame. Blitting both ways with the same vertex shader keeps the orientation self-consistent.
    uint _scratchTex, _scratchFbo;
    int _scratchW, _scratchH;

    // Rec.709 luminance, the weighting the rest of the pipeline uses for perceived brightness, so saturation pivots around real luminance rather than a flat average.
    static readonly string BlitFragmentSource = GlslFiles.Load("Totk/PostProcess/ColorCorrection/Blit.frag");

    static readonly string FragmentSource = GlslFiles.Load("Totk/PostProcess/ColorCorrection/Main.frag");

    public ColorCorrectionPass(GL gl)
    {
        _gl = gl;
        _program = GLProgramBuilder.Build(gl, FullscreenShaders.Vertex330, FragmentSource, "color_correction");
        _blitProgram = GLProgramBuilder.Build(gl, FullscreenShaders.Vertex330, BlitFragmentSource, "color_correction_blit");
    }

    public unsafe bool Run(GLResourceCache resources, RenderTargets targets, GpuTexture target,
        ColorCorrectionPostFx cc)
    {
        if (cc.IsIdentity)
            return false;

        EnsureScratch(target.Width, target.Height);

        // Pass 1: graded copy into a scratch target.
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

        // Pass 2: straight back through the same vertex shader, so whatever orientation pass 1 wrote, pass 2 reads identically and the round trip cannot flip.
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
        // Matches Ldr's Rgba32f: the grade runs on tonemapped but still float data, and a narrower scratch would quantise it.
        _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba32f, (uint)_scratchW, (uint)_scratchH, 0,
            PixelFormat.Rgba, PixelType.Float, null);
        _gl.SetSampling(TextureTarget.Texture2D, GLEnum.Nearest, GLEnum.ClampToEdge);
    }

    public void Dispose()
    {
        if (_program != 0) _gl.DeleteProgram(_program);
        if (_blitProgram != 0) _gl.DeleteProgram(_blitProgram);
        if (_scratchTex != 0) _gl.DeleteTexture(_scratchTex);
        if (_scratchFbo != 0) _gl.DeleteFramebuffer(_scratchFbo);
    }
}
