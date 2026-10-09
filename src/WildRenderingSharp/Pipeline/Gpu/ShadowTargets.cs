using Silk.NET.OpenGL;

namespace WildRenderingSharp.Pipeline.Gpu;

/// <summary>The fixed-size shadow map and cascade array and the depth-only framebuffers that draw into them, which outlive a viewport resize.</summary>
sealed class ShadowTargets : IDisposable
{
    public const int MapSize = 4096, CascadeSize = 2048, MaxCascades = 4;

    readonly GL _gl;
    readonly TargetTextureFactory _textures;
    readonly uint _mapFramebuffer;
    uint _cascadeFramebuffer;
    GpuTexture? _cascades;

    public ShadowTargets(GL gl, TargetTextureFactory textures)
    {
        _gl = gl;
        _textures = textures;
        Map = textures.ShadowMapDepth(MapSize);
        _mapFramebuffer = gl.GenFramebuffer();
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, _mapFramebuffer);
        gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, TextureTarget.Texture2D, Map.Handle, 0);
        // A depth-only framebuffer must say it has no colour output: the default draw buffer is COLOR_ATTACHMENT0, and naming an
        // attachment point with no image makes the framebuffer incomplete, discarding every draw and clear.
        gl.DrawBuffer(DrawBufferMode.None);
        gl.ReadBuffer(ReadBufferMode.None);
        Validate("shadow map");
    }

    public GpuTexture Map { get; }

    public GpuTexture Cascades => _cascades ??= CreateCascades();

    // Reports an incomplete shadow map framebuffer, for checking it is still complete once the viewport targets exist.
    public void Validate(string label)
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _mapFramebuffer);
        FramebufferCheck.Validate(_gl, label);
    }

    public void BindMap()
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _mapFramebuffer);
        _gl.Viewport(0, 0, MapSize, MapSize);
    }

    public void BindCascade(int cascade)
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _cascadeFramebuffer);
        _gl.FramebufferTextureLayer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, Cascades.Handle, 0, cascade);
        _gl.DrawBuffer(DrawBufferMode.None);
        _gl.ReadBuffer(ReadBufferMode.None);
        _gl.Viewport(0, 0, CascadeSize, CascadeSize);
    }

    public void Dispose()
    {
        _gl.DeleteTexture(Map.Handle);
        _gl.DeleteFramebuffer(_mapFramebuffer);
        if (_cascades is { } cascades)
        {
            _gl.DeleteTexture(cascades.Handle);
            _gl.DeleteFramebuffer(_cascadeFramebuffer);
        }
    }

    GpuTexture CreateCascades()
    {
        _cascadeFramebuffer = _gl.GenFramebuffer();
        return _textures.ShadowCascadeArray(CascadeSize, MaxCascades);
    }
}
