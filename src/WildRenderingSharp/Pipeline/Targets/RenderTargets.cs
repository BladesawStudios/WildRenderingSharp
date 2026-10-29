using System.Numerics;
using Silk.NET.OpenGL;
using WildRenderingSharp.Gpu;

namespace WildRenderingSharp.Pipeline.Targets;

/// <summary>Every size-dependent render target the deferred pipeline uses, plus the fixed-size shadow map.</summary>
public sealed class RenderTargets : IDisposable
{
    public const int BloomLevelCount = 4;
    public const int ShadowMapSize = ShadowTargets.MapSize, CascadeSize = ShadowTargets.CascadeSize, MaxCascades = ShadowTargets.MaxCascades;

    const int GBufferCount = 6;

    readonly GL _gl;
    readonly TargetTextureFactory _textures;
    readonly ShadowTargets _shadow;
    readonly PixelReadback _readback;
    readonly FrameSnapshots _snapshots;
    readonly uint _scratchColorFbo, _scratchColorDepthFbo;
    uint _gbufferFbo, _passIdFbo;

    public RenderTargets(GL gl, int width, int height)
    {
        _gl = gl;
        _textures = new TargetTextureFactory(gl);
        _shadow = new ShadowTargets(gl, _textures);
        _readback = new PixelReadback(gl);
        _snapshots = new FrameSnapshots(gl, _textures);
        _scratchColorFbo = gl.GenFramebuffer();
        _scratchColorDepthFbo = gl.GenFramebuffer();

        Resize(width, height);
        // The shadow map outlives resizes, so this establishes it is still complete once the viewport targets exist.
        _shadow.Validate("shadow map (post-resize)");
    }

    public int Width { get; private set; }
    public int Height { get; private set; }

    public GpuTexture[] GBuffer { get; private set; } = [];
    public GpuTexture GBufferDepth { get; private set; }

    public GpuTexture LinearDepth { get; private set; }
    public GpuTexture LinearDepthHalf { get; private set; }

    // Shadow and SSAO scratch.
    // The pre-shading buffers are half the frame's size, as the game's are; its resolve programs pick among their texels by depth.
    public GpuTexture PreShadow { get; private set; }
    public GpuTexture PreShadowScratch { get; private set; }
    public GpuTexture PreMisc { get; private set; }
    public GpuTexture VolumeMask { get; private set; }
    public GpuTexture PreFog { get; private set; }
    public GpuTexture PreFogHalf { get; private set; }
    public GpuTexture AoRaw { get; private set; }
    public GpuTexture AoTmp { get; private set; }

    public GpuTexture LightPrePassArray { get; private set; }

    // The light pre-pass as the field_* programs read it: layer 0 is the sky's ambient alone, which they multiply by albedo themselves.
    public GpuTexture FieldLightPrePassArray { get; private set; }

    public GpuTexture PassId { get; private set; }
    public GpuTexture PassIdDepth { get; private set; }

    // Resolve and tonemap chain.
    public GpuTexture ResolvePass { get; private set; }
    public GpuTexture Final { get; private set; }
    public GpuTexture Exposed { get; private set; }
    public GpuTexture Bloom { get; private set; }
    public GpuTexture Ldr { get; private set; }

    // Forward-pass scratch.
    public GpuTexture Scene { get; private set; }
    public GpuTexture Behind { get; private set; }
    public GpuTexture MaterialIdCopy { get; private set; }

    // Bloom pyramid: four levels, halved per level.
    public GpuTexture[] BloomLevels { get; private set; } = [];
    public GpuTexture[] BloomTmp { get; private set; } = [];

    public GpuTexture ShadowMap => _shadow.Map;
    public GpuTexture ShadowCascades => _shadow.Cascades;

    public void Resize(int width, int height)
    {
        width = Math.Max(8, width);
        height = Math.Max(8, height);
        if (width == Width && height == Height && GBuffer.Length > 0)
            return;
        (Width, Height) = (width, height);

        ReleaseViewportTargets();
        CreateGBuffer(width, height);
        CreateLightingBuffers(width, height);
        CreatePassIdMask(width, height);
        CreateColorChain(width, height);
        CreateBloomPyramid(width, height);
    }

    // Copies Final as it stands into stage slot slot, for looking at the frame between passes.
    public void SnapshotFinal(int slot) => _snapshots.Capture(slot, Final);

    // Copies what the deferred pass just resolved into stage slot 3.
    public void SnapshotResolve() => _snapshots.Capture(FrameSnapshots.Slots - 1, ResolvePass);

    // The frame as SnapshotFinal last saw it, or null.
    public GpuTexture? Stage(int slot) => _snapshots.Stage(slot);

    // One layer of a texture array as a plain texture, for looking at it. Overwritten by the next call.
    public GpuTexture LayerCopy(GpuTexture array, int layer) => _snapshots.LayerCopy(array, layer);

    public (GpuTexture Albedo, GpuTexture Normal, GpuTexture Depth) TerrainUnderCopies() => _snapshots.TerrainUnderCopies(Width, Height);

    public GpuTexture TerrainUnderEmission() => _snapshots.TerrainUnderEmission(Width, Height);

    public void BindColorTarget(GpuTexture target)
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _scratchColorFbo);
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, target.Handle, 0);
        _gl.Viewport(0, 0, (uint)target.Width, (uint)target.Height);
    }

    public void BindColorAndDepthTarget(GpuTexture color, GpuTexture depth)
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _scratchColorDepthFbo);
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, color.Handle, 0);
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, TextureTarget.Texture2D, depth.Handle, 0);
        _gl.Viewport(0, 0, (uint)color.Width, (uint)color.Height);
    }

    public void BindColorTargetLayer(GpuTexture arrayTarget, int layer)
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _scratchColorFbo);
        _gl.FramebufferTextureLayer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, arrayTarget.Handle, 0, layer);
        _gl.Viewport(0, 0, (uint)arrayTarget.Width, (uint)arrayTarget.Height);
    }

    // The albedo attachment's number in the G-buffer.
    public const int AlbedoAttachment = 1;

    public void BindGBuffer()
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _gbufferFbo);

        // Only the albedo attachment is sRGB, so this converts what is written to it and leaves the rest as they are.
        _gl.Enable(EnableCap.FramebufferSrgb);
        _gl.Viewport(0, 0, (uint)Width, (uint)Height);
    }

    public void SetGBufferColorMask(bool enabled)
    {
        for (uint i = 0; i < GBufferCount; i++)
            _gl.ColorMask(i, enabled, enabled, enabled, enabled);
    }

    // Sends the bound G-buffer's output locations to the attachments given, in order, and the rest nowhere; no attachments sends them to their own.
    public void SetGBufferOutputs(params ReadOnlySpan<int> attachments)
    {
        var buffers = new GLEnum[attachments.Length == 0 ? GBufferCount : attachments.Length];
        for (int i = 0; i < buffers.Length; i++)
            buffers[i] = GLEnum.ColorAttachment0 + (attachments.Length == 0 ? i : attachments[i]);
        _gl.DrawBuffers(buffers);
    }

    public void BindPassIdTarget()
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _passIdFbo);
        _gl.Viewport(0, 0, (uint)Width, (uint)Height);
    }

    public void BindShadowTarget() => _shadow.BindMap();

    public void BindShadowCascadeTarget(int cascade, bool statics = false) => _shadow.BindCascade(cascade, statics);

    public void CopyStaticShadowCascade(int cascade) => _shadow.CopyStaticToLive(cascade);

    public Vector4 ReadPixel(GpuTexture target, int x, int y) => _readback.Pixel(target, x, y);

    public byte[] ReadPixelsRgba8(GpuTexture target) => _readback.Rgba8(target);

    public float[] ReadPixelsFloatRgba(GpuTexture target) => _readback.FloatRgba(target);

    public void Dispose()
    {
        _textures.ReleaseOwned();
        _shadow.Dispose();
        _readback.Dispose();
        foreach (uint framebuffer in new[] { _gbufferFbo, _passIdFbo, _scratchColorFbo, _scratchColorDepthFbo })
            _gl.DeleteFramebuffer(framebuffer);
    }

    void ReleaseViewportTargets()
    {
        _textures.ReleaseOwned();
        _snapshots.Reset();
        if (_gbufferFbo != 0)
            _gl.DeleteFramebuffer(_gbufferFbo);
        if (_passIdFbo != 0)
            _gl.DeleteFramebuffer(_passIdFbo);
    }

    // Attachments 0-4 are 8-bit UNORM with nearest filtering because the resolve decodes packed flag bits from them.
    // Attachment 1, the albedo, is sRGB: the game's shaders write the linear albedo they sampled and read it back linear (the resolve does no gamma of its own), which only works with the hardware encoding it on
    // the way in and decoding it on the way out. Held as plain linear 8 bits, a dark albedo has a handful of levels per channel and shows as coloured squares wherever its texture varies.
    // Attachment 5 is emission and must be half-float, or it clamps at 1.0 and never crosses the bloom threshold.
    void CreateGBuffer(int width, int height)
    {
        GBuffer = new GpuTexture[GBufferCount];
        for (int i = 0; i < GBufferCount; i++)
            GBuffer[i] = i == GBufferCount - 1
                ? _textures.Color(width, height, InternalFormat.Rgba16f)
                : _textures.Color(width, height, i == AlbedoAttachment ? InternalFormat.Srgb8Alpha8 : InternalFormat.Rgba8, filterNearest: true);
        GBufferDepth = _textures.Depth(width, height);

        _gbufferFbo = _gl.GenFramebuffer();
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _gbufferFbo);
        var drawBuffers = new GLEnum[GBufferCount];
        for (int i = 0; i < GBufferCount; i++)
        {
            _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0 + i, TextureTarget.Texture2D, GBuffer[i].Handle, 0);
            drawBuffers[i] = GLEnum.ColorAttachment0 + i;
        }
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, TextureTarget.Texture2D, GBufferDepth.Handle, 0);
        _gl.DrawBuffers(drawBuffers);
        FramebufferCheck.Validate(_gl, "G-buffer");
    }

    // Linear depth is a scalar everywhere it is used, so R32F keeps its precision at a quarter of the storage; the rest are ordinary
    // HDR targets, where RGBA16F keeps values up to 65504 at half the cost of RGBA32F.
    void CreateLightingBuffers(int width, int height)
    {
        LinearDepth = _textures.Color(width, height, InternalFormat.R32f);
        LinearDepthHalf = _textures.Color(width / 2, height / 2, InternalFormat.R32f);

        int halfWidth = Math.Max(1, width / 2), halfHeight = Math.Max(1, height / 2);
        PreShadow = _textures.Color(halfWidth, halfHeight, InternalFormat.Rgba16f);
        PreShadowScratch = _textures.Color(halfWidth, halfHeight, InternalFormat.Rgba16f);
        PreMisc = _textures.Color(halfWidth, halfHeight, InternalFormat.Rgba16f);
        PreFog = _textures.Color(width, height, InternalFormat.Rgba16f);
        VolumeMask = _textures.Color(width, height, InternalFormat.Rgba8);
        PreFogHalf = _textures.Color(halfWidth, halfHeight, InternalFormat.Rgba16f);
        AoRaw = _textures.Color(width, height, InternalFormat.Rgba16f);
        AoTmp = _textures.Color(width, height, InternalFormat.Rgba16f);
        LightPrePassArray = _textures.ColorArray(halfWidth, halfHeight, InternalFormat.Rgba16f, layers: 2);
        FieldLightPrePassArray = _textures.ColorArray(halfWidth, halfHeight, InternalFormat.Rgba16f, layers: 2);
    }

    void CreatePassIdMask(int width, int height)
    {
        PassId = _textures.Color(width, height, InternalFormat.Rgba8, filterNearest: true);
        PassIdDepth = _textures.Depth(width, height);
        _passIdFbo = _gl.GenFramebuffer();
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _passIdFbo);
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, PassId.Handle, 0);
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, TextureTarget.Texture2D, PassIdDepth.Handle, 0);
        FramebufferCheck.Validate(_gl, "pass-ID mask");
    }

    void CreateColorChain(int width, int height)
    {
        ResolvePass = _textures.Color(width, height, InternalFormat.Rgba16f);
        Final = _textures.Color(width, height, InternalFormat.Rgba16f);
        Exposed = _textures.Color(width, height, InternalFormat.Rgba16f);
        Bloom = _textures.Color(width, height, InternalFormat.Rgba16f);
        Ldr = _textures.Color(width, height, InternalFormat.Rgba16f);

        Scene = _textures.Color(width, height, InternalFormat.Rgba16f);
        // Clamped: it is sampled as cTex_ColorBuffer at refracted positions that can land past the edge, where wrapping would show the opposite side of the screen.
        Behind = _textures.Color(width, height, InternalFormat.Rgba16f, repeat: false);
        MaterialIdCopy = _textures.Color(width, height, InternalFormat.Rgba8, repeat: false, filterNearest: true);
    }

    void CreateBloomPyramid(int width, int height)
    {
        BloomLevels = new GpuTexture[BloomLevelCount];
        BloomTmp = new GpuTexture[BloomLevelCount];
        int levelWidth = width / 2, levelHeight = height / 2;
        for (int i = 0; i < BloomLevelCount; i++)
        {
            BloomLevels[i] = _textures.Color(levelWidth, levelHeight, InternalFormat.Rgba16f, repeat: false);
            BloomTmp[i] = _textures.Color(levelWidth, levelHeight, InternalFormat.Rgba16f, repeat: false);
            levelWidth = Math.Max(1, levelWidth / 2);
            levelHeight = Math.Max(1, levelHeight / 2);
        }
    }
}
