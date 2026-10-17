using WildRenderingSharp.Gpu;
using WildRenderingSharp.Graphics.Ubos;
using Silk.NET.OpenGL;
using WildRenderingSharp.Pipeline.Passes;
using WildRenderingSharp.Pipeline.Resources;
using WildRenderingSharp.Pipeline.Targets;
using WildRenderingSharp.Shaders;

namespace WildRenderingSharp.Profiles.Totk.Deferred.Resolve;

/// <summary>
/// Exposure, then the game's <c>agl_hdr_compose</c> (loaded through <see cref="ShaderProgramCache"/> like any decompiled game shader) over the scene and
/// the bloom. The compose is the game's tonemap, so the HDR reaches it, and the bloom's bright pass, as the game's does, uncompressed.
/// </summary>
internal sealed class TonemapPass : IDisposable
{
    readonly GL _gl;
    readonly uint _exposureProgram;
    uint _hdrQuadVbo, _hdrQuadVao;
    uint _hdrQuadVaoProgram;

    static readonly string ExposureFragmentSource = GlslFiles.Load("Totk/Deferred/Tonemap/Exposure.frag");

    public TonemapPass(GL gl)
    {
        _gl = gl;
        _exposureProgram = GLProgramBuilder.Build(gl, FullscreenShaders.Vertex450, ExposureFragmentSource, "exposure");

        // hdr_compose's vertex shader is attribute-driven, unlike the fullscreen-triangle passes above: in_attr0 is a half-size position it
        // doubles into clip space, in_attr1 the UV.
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

    // The frame's HDR with the exposure applied.
    public GpuTexture RunExposure(GLResourceCache resources, RenderTargets targets, float exposure)
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
        return hdrInput;
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
        _gl.ReleaseProgram(_exposureProgram);
        _gl.DeleteBuffer(_hdrQuadVbo);
        if (_hdrQuadVao != 0)
            _gl.DeleteVertexArray(_hdrQuadVao);
    }
}
