using WildRenderingSharp.Gpu;
using WildRenderingSharp.Graphics.Ubos;
using Silk.NET.OpenGL;
using WildRenderingSharp.Pipeline.Passes;
using WildRenderingSharp.Pipeline.Resources;
using WildRenderingSharp.Pipeline.Targets;
using WildRenderingSharp.Shaders;

namespace WildRenderingSharp.Profiles.Totk.Deferred.Resolve;

/// <summary>
/// Exposure, highlight compression, the game's <c>agl_hdr_compose</c> (loaded through <see cref="ShaderProgramCache"/> like any
/// decompiled game shader), then the bloom add.
/// </summary>
internal sealed class TonemapPass : IDisposable
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

    static readonly string ExposureFragmentSource = GlslFiles.Load("Totk/Deferred/Tonemap/Exposure.frag");

    // Compresses on the brightest channel and scales the colour as a whole. Per-channel compression desaturates by construction: for a saturated colour only the dominant channel exceeds the knee, so it
    // alone is pulled down and the three converge toward grey (a blood-moon red (3.0, 0.6, 0.45) went from saturation 0.850 to 0.772 at this step alone, the "deep red comes out pink" failure). Scaling
    // by compressed/max keeps every channel ratio, so hue and saturation are preserved while the magnitude lands under the ceiling. Neutral colours are unaffected.
    static readonly string CompressFragmentSource = GlslFiles.Load("Totk/Deferred/Tonemap/Compress.frag");

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

    public void RunHdrComposite(GLResourceCache resources, RenderTargets targets, uint hdrComposeProgram, GpuTexture hdrSource, GpuTexture bloomSource, Ubo hdrComposeParams)
    {
        EnsureHdrQuadVao(hdrComposeProgram);
        resources.Bind(hdrComposeParams);

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
