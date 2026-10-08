using Silk.NET.OpenGL;
using WildRenderingSharp.Assets;
using WildRenderingSharp.Pipeline;

namespace WildRenderingSharp.Profiles.Totk.Deferred;

/// <summary>
/// Exposure, highlight compression, the game's <c>agl_hdr_compose</c> (loaded through <see cref="ShaderProgramCache"/> like any decompiled game shader), then the bloom add. The highlight compression is
/// this renderer's own (not decompiled): it softly asymptotes the rare grazing silhouette highlight that saturates to its coded ceiling under a high calibrated exposure (see <c>HDR_COMPRESS_SRC</c>).
/// Split into two calls because <see cref="BloomPass"/> must run between them, reading the compressed result.
/// </summary>
public sealed class TonemapPass : IDisposable
{
    readonly GL _gl;
    readonly uint _exposureProgram, _compressProgram;
    uint _hdrQuadVbo, _hdrQuadVao;
    uint _hdrQuadVaoProgram;

    // The compression asymptotes to knee + (ceil - knee) = HdrCompressCeil, the brightest value that can leave this pass. It was 2.2, which a display cannot show: values from 1 to 2.2 survived the
    // tonemap only to be clipped at present, and clipping the dominant channel shifts the ratio (a red sky at (2.5, 0, 0.55) presents as (1, 0, 0.42), nearly double the relative blue, toward magenta).
    // Asymptoting to 1.0 means nothing needs clipping later and the computed hue reaches the screen.
    public const float HdrCompressKnee = 0.8f;
    public const float HdrCompressCeil = 1.0f;

    const string ExposureFragmentSource = """
        #version 450 core
        uniform sampler2D t; uniform float k; in vec2 vUV; out vec4 fragColor;
        void main() { fragColor = vec4(texture(t, vUV).rgb * k, 1.0); }
        """;

    // Compresses on the brightest channel and scales the colour as a whole. Per-channel compression desaturates by construction: for a saturated colour only the dominant channel exceeds the knee, so it
    // alone is pulled down and the three converge toward grey (a blood-moon red (3.0, 0.6, 0.45) went from saturation 0.850 to 0.772 at this step alone, the "deep red comes out pink" failure). Scaling
    // by compressed/max keeps every channel ratio, so hue and saturation are preserved while the magnitude lands under the ceiling. Neutral colours are unaffected.
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
        _exposureProgram = GLProgramBuilder.Build(gl, FullscreenShaders.Vertex450, ExposureFragmentSource, "exposure");
        _compressProgram = GLProgramBuilder.Build(gl, FullscreenShaders.Vertex450, CompressFragmentSource, "hdr_compress");

        // hdr_compose's vertex shader is attribute-driven, unlike the fullscreen-triangle passes above: in_attr0 is a half-size position it doubles into clip space, in_attr1 the UV.
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

    /// <summary>Exposure multiply (skipped when 1.0) then the highlight-compression knee and ceiling. Returns the resulting texture.</summary>
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

    /// <summary>The <c>agl_hdr_compose</c> draw. Its samplers (<c>fp_t_tcb_8</c> = cColor, <c>fp_t_tcb_A</c> = cBloom) carry an explicit <c>layout(binding=N)</c> from the BNSH reflection, so binding the texture unit is enough, as for every decompiled game shader.</summary>
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
