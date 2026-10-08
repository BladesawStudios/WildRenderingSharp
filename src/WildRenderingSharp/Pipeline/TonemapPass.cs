using Silk.NET.OpenGL;
using WildRenderingSharp.Assets;

namespace WildRenderingSharp.Pipeline;

/// <summary>
/// Exposure -> highlight compression -> the real <c>agl_hdr_compose</c> (TotK's own HDR compose
/// pass, loaded through <see cref="ShaderProgramCache"/> like any other decompiled game shader) ->
/// bloom add. The highlight compression is WildRenderingSharp's own addition (not decompiled), softly
/// asymptoting the rare silhouette-adjacent grazing highlight that legally saturates to its own
/// coded ceiling under a high calibrated exposure - see <c>HDR_COMPRESS_SRC</c>'s remarks for why.
///
/// Split into two calls because <see cref="BloomPass"/> must run in between, reading this pass's
/// compressed HDR result as its own input.
/// </summary>
public sealed class TonemapPass : IDisposable
{
    readonly GL _gl;
    readonly uint _exposureProgram, _compressProgram;
    uint _hdrQuadVbo, _hdrQuadVao;
    uint _hdrQuadVaoProgram;

    // The compression asymptotes to knee + (ceil - knee) = HdrCompressCeil, so the ceiling is the
    // brightest value that can leave this pass. It used to be 2.2, which a display cannot show -
    // everything between 1 and 2.2 survived tonemapping only to be clipped at final present, and
    // clipping the dominant channel while the others pass through shifts the ratio (a red sky at
    // (2.5, 0, 0.55) presents as (1, 0, 0.42) - nearly DOUBLE the relative blue, i.e. a push toward
    // magenta). Asymptoting to 1.0 instead means nothing needs clipping later and the hue that
    // reaches the screen is the hue that was computed.
    public const float HdrCompressKnee = 0.8f;
    public const float HdrCompressCeil = 1.0f;

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

    const string ExposureFragmentSource = """
        #version 450 core
        uniform sampler2D t; uniform float k; in vec2 vUV; out vec4 fragColor;
        void main() { fragColor = vec4(texture(t, vUV).rgb * k, 1.0); }
        """;

    // Compresses on the BRIGHTEST CHANNEL and scales the colour as a whole, rather than squashing
    // each channel independently.
    //
    // Per-channel was the previous behaviour and it desaturates by construction: for a saturated
    // colour only the dominant channel exceeds the knee, so only that one is pulled down while the
    // others pass through untouched, and the three converge toward grey. Measured on a blood-moon
    // red (3.0, 0.6, 0.45): saturation 0.850 -> 0.772 at this step alone, before agl_hdr_compose
    // even runs. That is the "deep red comes out pink" failure, and no amount of upstream colour
    // correction can survive it.
    //
    // Scaling by compressed/max keeps every channel ratio identical, so hue and saturation are
    // preserved exactly while the magnitude still lands under the ceiling. Neutral colours are
    // unaffected either way, so this changes nothing for non-saturated content.
    const string CompressFragmentSource = """
        #version 450 core
        uniform sampler2D t; uniform float uKnee, uCeil;
        in vec2 vUV; out vec4 fragColor;
        void main() {
            vec3 c = texture(t, vUV).rgb;
            float m = max(max(c.r, c.g), c.b);
            if (m > uKnee) {
                float excess = m - uKnee;
                float range = max(uCeil - uKnee, 1e-4);
                float compressed = uKnee + excess / (1.0 + excess / range);
                c *= compressed / max(m, 1e-6);
            }
            fragColor = vec4(c, 1.0);
        }
        """;

    public TonemapPass(GL gl)
    {
        _gl = gl;
        _exposureProgram = GLProgramBuilder.Build(gl, QuadVertexSource, ExposureFragmentSource, "exposure");
        _compressProgram = GLProgramBuilder.Build(gl, QuadVertexSource, CompressFragmentSource, "hdr_compress");

        // hdr_compose's own vertex shader is attribute-driven (unlike the fullscreen-triangle
        // passes above): in_attr0 is a half-size position it doubles into clip space, in_attr1
        // the UV.
        float[] quad =
        [
            -0.5f, -0.5f, 0f, 1f, 0f, 0f, 0f, 0f,
             0.5f, -0.5f, 0f, 1f, 1f, 0f, 0f, 0f,
            -0.5f,  0.5f, 0f, 1f, 0f, 1f, 0f, 0f,
             0.5f,  0.5f, 0f, 1f, 1f, 1f, 0f, 0f,
        ];
        _hdrQuadVbo = GLBuffer.Create(gl, BufferTargetARB.ArrayBuffer, MemoryMarshalBytes(quad));
    }

    static ReadOnlySpan<byte> MemoryMarshalBytes(float[] values) => System.Runtime.InteropServices.MemoryMarshal.AsBytes<float>(values);

    /// <summary>Exposure multiply (skipped when 1.0, matching <c>render_scene</c>) then the highlight-compression knee/ceiling. Returns the resulting texture (either <c>targets.Compressed</c> always).</summary>
    public GpuTexture RunExposureAndCompress(GLResourceCache resources, RenderTargets targets, float exposure)
    {
        _gl.Disable(EnableCap.DepthTest);
        GpuTexture hdrInput = targets.Final;
        if (exposure != 1.0f)
        {
            _gl.UseProgram(_exposureProgram);
            targets.BindColorTarget(targets.Exposed);
            _gl.BindTextureUniform(_exposureProgram, "t", 0, targets.Final.Handle);
            _gl.SetFloat(_exposureProgram, "k", exposure);
            resources.DrawFullscreenTriangle();
            hdrInput = targets.Exposed;
        }

        _gl.UseProgram(_compressProgram);
        targets.BindColorTarget(targets.Compressed);
        _gl.BindTextureUniform(_compressProgram, "t", 0, hdrInput.Handle);
        _gl.SetFloat(_compressProgram, "uKnee", HdrCompressKnee);
        _gl.SetFloat(_compressProgram, "uCeil", HdrCompressCeil);
        resources.DrawFullscreenTriangle();
        return targets.Compressed;
    }

    /// <summary>
    /// The real <c>agl_hdr_compose</c> draw. Its sampler inputs (<c>fp_t_tcb_8</c> = cColor,
    /// <c>fp_t_tcb_A</c> = cBloom) already carry an explicit <c>layout(binding=N)</c> from the
    /// BNSH reflection, so - like every other decompiled game shader here - binding the texture
    /// UNIT is enough; no separate sampler-uniform assignment is needed or correct.
    /// </summary>
    public void RunHdrComposite(GLResourceCache resources, RenderTargets targets, uint hdrComposeProgram, GpuTexture hdrSource, GpuTexture bloomSource, byte[] hdrComposeParamsBytes)
    {
        EnsureHdrQuadVao(hdrComposeProgram);
        resources.Ubo("hdr_compose_params", hdrComposeParamsBytes, bindingIndex: Profiles.Totk.TotkBindings.HdrComposeParams);

        targets.BindColorTarget(targets.Ldr);
        _gl.ClearColor(0, 0, 0, 1);
        _gl.Clear(ClearBufferMask.ColorBufferBit);
        _gl.UseProgram(hdrComposeProgram);
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, hdrSource.Handle);
        _gl.ActiveTexture(TextureUnit.Texture1);
        _gl.BindTexture(TextureTarget.Texture2D, bloomSource.Handle);

        _gl.BindVertexArray(_hdrQuadVao);
        _gl.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
    }

    unsafe void EnsureHdrQuadVao(uint program)
    {
        if (_hdrQuadVao != 0 && _hdrQuadVaoProgram == program)
            return;
        if (_hdrQuadVao != 0)
            _gl.DeleteVertexArray(_hdrQuadVao);

        _hdrQuadVao = _gl.GenVertexArray();
        _gl.BindVertexArray(_hdrQuadVao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _hdrQuadVbo);
        int posLoc = _gl.GetAttribLocation(program, "in_attr0");
        int uvLoc = _gl.GetAttribLocation(program, "in_attr1");
        const uint stride = 8 * sizeof(float);
        if (posLoc >= 0)
        {
            _gl.EnableVertexAttribArray((uint)posLoc);
            _gl.VertexAttribPointer((uint)posLoc, 4, VertexAttribPointerType.Float, false, stride, (void*)0);
        }
        if (uvLoc >= 0)
        {
            _gl.EnableVertexAttribArray((uint)uvLoc);
            _gl.VertexAttribPointer((uint)uvLoc, 4, VertexAttribPointerType.Float, false, stride, (void*)(4 * sizeof(float)));
        }
        _gl.BindVertexArray(0);
        _hdrQuadVaoProgram = program;
    }

    public void Dispose()
    {
        _gl.DeleteProgram(_exposureProgram);
        _gl.DeleteProgram(_compressProgram);
        _gl.DeleteBuffer(_hdrQuadVbo);
        if (_hdrQuadVao != 0)
            _gl.DeleteVertexArray(_hdrQuadVao);
    }
}
