using Silk.NET.OpenGL;
using WildRenderingSharp.Gpu;

namespace WildRenderingSharp.Pipeline.Targets;

/// <summary>The fixed-size shadow map and cascade array and the depth-only framebuffers that draw into them, which outlive a viewport resize.</summary>
sealed class ShadowTargets : IDisposable
{
    public const int MapSize = 4096, CascadeSize = 2048, MaxCascades = 4;

    readonly GL _gl;
    readonly TargetTextureFactory _textures;
    readonly uint _mapFramebuffer;
    uint _cascadeFramebuffer, _staticFramebuffer;
    GpuTexture? _cascades, _staticCascades;

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

    // What each cascade holds of the things that do not move, kept apart so the moving ones can be drawn over a copy of it every frame.
    GpuTexture StaticCascades => _staticCascades ??= CreateStaticCascades();

    // Puts a cascade's static depth into the cascade the resolve reads.
    public void CopyStaticToLive(int cascade) =>
        _gl.CopyImageSubData(StaticCascades.Handle, CopyImageSubDataTarget.Texture2DArray, 0, 0, 0, cascade,
            Cascades.Handle, CopyImageSubDataTarget.Texture2DArray, 0, 0, 0, cascade, CascadeSize, CascadeSize, 1);

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

    // Draws into a cascade of the array the resolve reads, or into the same cascade of the array that keeps what does not move.
    public void BindCascade(int cascade, bool statics = false)
    {
        // Read first: the framebuffer is created along with the array, on first use.
        uint cascades = statics ? StaticCascades.Handle : Cascades.Handle;
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, statics ? _staticFramebuffer : _cascadeFramebuffer);
        _gl.FramebufferTextureLayer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, cascades, 0, cascade);
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
        if (_staticCascades is { } statics)
        {
            _gl.DeleteTexture(statics.Handle);
            _gl.DeleteFramebuffer(_staticFramebuffer);
        }
    }

    GpuTexture CreateStaticCascades()
    {
        _staticFramebuffer = _gl.GenFramebuffer();
        return _textures.ShadowCascadeArray(CascadeSize, MaxCascades);
    }

    GpuTexture CreateCascades()
    {
        _cascadeFramebuffer = _gl.GenFramebuffer();
        return _textures.ShadowCascadeArray(CascadeSize, MaxCascades);
    }
}
