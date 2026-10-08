using Silk.NET.OpenGL;

namespace WildRenderingSharp.Pipeline;

/// <summary>
/// Every size-dependent render target the deferred pipeline uses, plus the fixed-size shadow map. Rebuilt when the viewport
/// resizes. Framebuffers are two shared scratch objects repointed per pass with <c>glFramebufferTexture2D</c>, rather than one per
/// texture combination.
/// </summary>
public sealed class RenderTargets : IDisposable
{
    readonly GL _gl;

    public int Width { get; private set; }
    public int Height { get; private set; }

    // G-buffer.
    public GpuTexture[] GBuffer { get; private set; } = [];
    public GpuTexture GBufferDepth { get; private set; }
    uint _gbufferFbo;

    // Linear depth.
    public GpuTexture LinearDepth { get; private set; }
    public GpuTexture LinearDepthHalf { get; private set; }

    // Shadow and SSAO scratch.
    public GpuTexture PreShadow { get; private set; }
    public GpuTexture PreShadowHalf0 { get; private set; }
    public GpuTexture PreShadowHalf1 { get; private set; }
    public GpuTexture PreMisc { get; private set; }
    public GpuTexture AoRaw { get; private set; }
    public GpuTexture AoTmp { get; private set; }

    /// <summary>
    /// Per-pixel lighting for <c>cTex_DeferredLightPrePass</c> (binding 28), a <c>sampler2DArray</c> every <c>chara_*</c> resolve
    /// shader samples at layer 0 for its main light colour (see <see cref="LightPrePass"/>). Two layers to match the declared array
    /// size; only layer 0 has a confirmed reader, so layer 1 stays zero.
    /// </summary>
    public GpuTexture LightPrePassArray { get; private set; }

    // Pass-ID mask.
    public GpuTexture PassId { get; private set; }
    public GpuTexture PassIdDepth { get; private set; }
    uint _passIdFbo;

    // Resolve and tonemap chain.
    public GpuTexture ResolvePass { get; private set; }
    public GpuTexture Final { get; private set; }
    public GpuTexture Exposed { get; private set; }
    public GpuTexture Compressed { get; private set; }
    public GpuTexture Bloom { get; private set; }
    public GpuTexture Ldr { get; private set; }

    // Forward-pass scratch.
    public GpuTexture Scene { get; private set; }
    public GpuTexture Behind { get; private set; }

    GpuTexture? _underAlbedo, _underNormal, _underDepth;

    /// <summary>
    /// Copies of the G-buffer albedo, normal and depth under the terrain, made the first time a frame has terrain and remade with
    /// the other targets.
    /// </summary>
    public (GpuTexture Albedo, GpuTexture Normal, GpuTexture Depth) TerrainUnderCopies()
    {
        _underAlbedo ??= CreateColorTexture(Width, Height, InternalFormat.Rgba8, repeat: false, filterNearest: true);
        _underNormal ??= CreateColorTexture(Width, Height, InternalFormat.Rgba8, repeat: false, filterNearest: true);
        _underDepth ??= CreateColorTexture(Width, Height, InternalFormat.R32f, repeat: false);
        return (_underAlbedo.Value, _underNormal.Value, _underDepth.Value);
    }

    /// <summary>
    /// A copy of G-buffer attachment 0 (<c>cTex_GBuffMaterialID</c>) for shapes drawn over the opaque G-buffer; see
    /// <c>SceneColorShapePass</c>.
    /// </summary>
    public GpuTexture MaterialIdCopy { get; private set; }

    // Bloom pyramid: four levels, halved per level.
    public GpuTexture[] BloomLevels { get; private set; } = [];
    public GpuTexture[] BloomTmp { get; private set; } = [];
    public const int BloomLevelCount = 4;

    // Shadow map: fixed size, independent of the viewport.
    public const int ShadowMapSize = 4096;
    public GpuTexture ShadowMap { get; }
    uint _shadowFbo;

    readonly List<uint> _owned = [];
    // Textures that do not depend on the viewport and must survive a resize, like the shadow map. Kept in _owned they were
    // deleted by the constructor's own Resize, leaving the shadow framebuffer incomplete.
    readonly List<uint> _ownedFixedSize = [];
    uint _scratchColorFbo, _scratchColorDepthFbo;

    public RenderTargets(GL gl, int width, int height)
    {
        _gl = gl;
        _scratchColorFbo = gl.GenFramebuffer();
        _scratchColorDepthFbo = gl.GenFramebuffer();

        ShadowMap = CreateDepthTexture(ShadowMapSize, ShadowMapSize, resizeOwned: false, isShadowMap: true);
        _shadowFbo = gl.GenFramebuffer();
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, _shadowFbo);
        gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, TextureTarget.Texture2D, ShadowMap.Handle, 0);
        // A depth-only framebuffer must say it has no colour output: the default draw buffer is COLOR_ATTACHMENT0, and
        // naming an attachment point with no image makes the framebuffer incomplete, discarding every draw and clear.
        gl.DrawBuffer(DrawBufferMode.None);
        gl.ReadBuffer(ReadBufferMode.None);
        ValidateFramebuffer(gl, "shadow map");

        Resize(width, height);

        // Checked again after the first Resize: the shadow map outlives resizes, so this establishes it is still complete once the viewport targets exist.
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, _shadowFbo);
        ValidateFramebuffer(gl, "shadow map (post-resize)");
    }

    public void Resize(int width, int height)
    {
        width = Math.Max(8, width);
        height = Math.Max(8, height);
        if (width == Width && height == Height && GBuffer.Length > 0)
            return;
        Width = width;
        Height = height;

        foreach (uint t in _owned)
            _gl.DeleteTexture(t);
        _owned.Clear();
        _underAlbedo = _underNormal = _underDepth = null;
        if (_gbufferFbo != 0) _gl.DeleteFramebuffer(_gbufferFbo);
        if (_passIdFbo != 0) _gl.DeleteFramebuffer(_passIdFbo);

        // Attachments 0-4 are 8-bit UNORM: the resolve decodes packed flag bits with trunc(v * 255), which needs exactly
        // 8-bit quantisation, and nearest filtering so bit-packed data is never interpolated between texels. Attachment 5
        // is emission and must be half-float, or it clamps at 1.0 and never crosses the bloom threshold.
        GBuffer = new GpuTexture[6];
        for (int i = 0; i < 6; i++)
            GBuffer[i] = i == 5 ? CreateColorTexture(width, height, InternalFormat.Rgba16f) : CreateColorTexture(width, height, InternalFormat.Rgba8, filterNearest: true);
        GBufferDepth = CreateDepthTexture(width, height);
        _gbufferFbo = _gl.GenFramebuffer();
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _gbufferFbo);
        var drawBuffers = new GLEnum[6];
        for (int i = 0; i < 6; i++)
        {
            _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0 + i, TextureTarget.Texture2D, GBuffer[i].Handle, 0);
            drawBuffers[i] = GLEnum.ColorAttachment0 + i;
        }
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, TextureTarget.Texture2D, GBufferDepth.Handle, 0);
        _gl.DrawBuffers(drawBuffers);
        ValidateFramebuffer(_gl, "G-buffer");

        // Scalar everywhere they are used: R32F keeps the 32-bit precision at a quarter of the storage.
        LinearDepth = CreateColorTexture(width, height, InternalFormat.R32f);
        LinearDepthHalf = CreateColorTexture(width / 2, height / 2, InternalFormat.R32f);

        // The remaining post-process buffers are ordinary HDR targets; RGBA16F keeps values up to 65504 at half the cost of RGBA32F.
        PreShadow = CreateColorTexture(width, height, InternalFormat.Rgba16f);
        int halfW = Math.Max(1, width / 2);
        int halfH = Math.Max(1, height / 2);
        PreShadowHalf0 = CreateColorTexture(halfW, halfH, InternalFormat.Rgba16f, repeat: false);
        PreShadowHalf1 = CreateColorTexture(halfW, halfH, InternalFormat.Rgba16f, repeat: false);
        PreMisc = CreateColorTexture(width, height, InternalFormat.Rgba16f);
        AoRaw = CreateColorTexture(width, height, InternalFormat.Rgba16f);
        AoTmp = CreateColorTexture(width, height, InternalFormat.Rgba16f);
        LightPrePassArray = CreateColorTextureArray(width, height, InternalFormat.Rgba16f, layers: 2);

        PassId = CreateColorTexture(width, height, InternalFormat.Rgba8, filterNearest: true);
        PassIdDepth = CreateDepthTexture(width, height);
        _passIdFbo = _gl.GenFramebuffer();
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _passIdFbo);
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, PassId.Handle, 0);
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, TextureTarget.Texture2D, PassIdDepth.Handle, 0);
        ValidateFramebuffer(_gl, "pass-ID mask");

        ResolvePass = CreateColorTexture(width, height, InternalFormat.Rgba16f);
        Final = CreateColorTexture(width, height, InternalFormat.Rgba16f);
        Exposed = CreateColorTexture(width, height, InternalFormat.Rgba16f);
        Compressed = CreateColorTexture(width, height, InternalFormat.Rgba16f);
        Bloom = CreateColorTexture(width, height, InternalFormat.Rgba16f);
        Ldr = CreateColorTexture(width, height, InternalFormat.Rgba16f);

        Scene = CreateColorTexture(width, height, InternalFormat.Rgba16f);
        // Clamped: it is sampled as cTex_ColorBuffer at refracted positions that can land past the edge, where wrapping would show the opposite side of the screen.
        Behind = CreateColorTexture(width, height, InternalFormat.Rgba16f, repeat: false);
        MaterialIdCopy = CreateColorTexture(width, height, InternalFormat.Rgba8, repeat: false, filterNearest: true);

        BloomLevels = new GpuTexture[BloomLevelCount];
        BloomTmp = new GpuTexture[BloomLevelCount];
        int bw = width / 2, bh = height / 2;
        for (int i = 0; i < BloomLevelCount; i++)
        {
            BloomLevels[i] = CreateColorTexture(bw, bh, InternalFormat.Rgba16f, repeat: false);
            BloomTmp[i] = CreateColorTexture(bw, bh, InternalFormat.Rgba16f, repeat: false);
            bw = Math.Max(1, bw / 2);
            bh = Math.Max(1, bh / 2);
        }
    }

    /// <summary>
    /// Retargets the shared single-color-attachment scratch framebuffer to <paramref name="target"/> and sets the viewport to its
    /// size.
    /// </summary>
    public void BindColorTarget(GpuTexture target)
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _scratchColorFbo);
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, target.Handle, 0);
        _gl.Viewport(0, 0, (uint)target.Width, (uint)target.Height);
    }

    /// <summary>
    /// Retargets the shared color+depth scratch framebuffer - for the forward pass, which depth-tests against the G-buffer depth it
    /// was rasterised alongside.
    /// </summary>
    public void BindColorAndDepthTarget(GpuTexture color, GpuTexture depth)
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _scratchColorDepthFbo);
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, color.Handle, 0);
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, TextureTarget.Texture2D, depth.Handle, 0);
        _gl.Viewport(0, 0, (uint)color.Width, (uint)color.Height);
    }

    /// <summary>
    /// Retargets the shared single-color-attachment scratch framebuffer to one LAYER of a texture array (e.g. <see
    /// cref="LightPrePassArray"/>) via <c>glFramebufferTextureLayer</c>, rather than a whole <c>Texture2D</c>.
    /// </summary>
    public void BindColorTargetLayer(GpuTexture arrayTarget, int layer)
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _scratchColorFbo);
        _gl.FramebufferTextureLayer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, arrayTarget.Handle, 0, layer);
        _gl.Viewport(0, 0, (uint)arrayTarget.Width, (uint)arrayTarget.Height);
    }

    /// <summary>
    /// Reads back the exact RGBA float value of one pixel, for a numeric probe of what a shader value is really doing: a screenshot
    /// has been through exposure and the tonemap, so merely bright and extremely bright both look white. Read from a scene-referred
    /// target like <c>Final</c>.
    /// </summary>
    public unsafe System.Numerics.Vector4 ReadPixel(GpuTexture target, int x, int y)
    {
        x = Math.Clamp(x, 0, target.Width - 1);
        y = Math.Clamp(y, 0, target.Height - 1);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _scratchColorFbo);
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, target.Handle, 0);
        float* pixel = stackalloc float[4];
        _gl.ReadPixels(x, y, 1, 1, PixelFormat.Rgba, PixelType.Float, pixel);
        return new System.Numerics.Vector4(pixel[0], pixel[1], pixel[2], pixel[3]);
    }

    /// <summary>
    /// Reads back a whole RGBA8 target as top-down rows (GL is bottom-left origin, flipped here). The bulk counterpart to <see
    /// cref="ReadPixel"/>; only meaningful for a tonemapped 8-bit target like <c>Ldr</c>.
    /// </summary>
    public unsafe byte[] ReadPixelsRgba8(GpuTexture target)
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _scratchColorFbo);
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, target.Handle, 0);

        var raw = new byte[target.Width * target.Height * 4];
        fixed (byte* p = raw)
            _gl.ReadPixels(0, 0, (uint)target.Width, (uint)target.Height, PixelFormat.Rgba, PixelType.UnsignedByte, p);

        var flipped = new byte[raw.Length];
        int stride = target.Width * 4;
        for (int y = 0; y < target.Height; y++)
            Array.Copy(raw, y * stride, flipped, (target.Height - 1 - y) * stride, stride);
        return flipped;
    }

    /// <summary>
    /// Reads back a whole RGBA32F target as top-down rows of raw linear floats, with no clamping or tonemap. Meant for a
    /// scene-referred target like <c>Final</c>, so an HDR export carries the scene's dynamic range for the opening tool to expose.
    /// </summary>
    public unsafe float[] ReadPixelsFloatRgba(GpuTexture target)
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _scratchColorFbo);
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, target.Handle, 0);

        var raw = new float[target.Width * target.Height * 4];
        fixed (float* p = raw)
            _gl.ReadPixels(0, 0, (uint)target.Width, (uint)target.Height, PixelFormat.Rgba, PixelType.Float, p);

        var flipped = new float[raw.Length];
        int stride = target.Width * 4;
        for (int y = 0; y < target.Height; y++)
            Array.Copy(raw, y * stride, flipped, (target.Height - 1 - y) * stride, stride);
        return flipped;
    }

    public void BindGBuffer()
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _gbufferFbo);
        _gl.Viewport(0, 0, (uint)Width, (uint)Height);
    }

    public void SetGBufferColorMask(bool enabled)
    {
        for (uint i = 0; i < 6; i++)
            _gl.ColorMask(i, enabled, enabled, enabled, enabled);
    }

    public void BindPassIdTarget()
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _passIdFbo);
        _gl.Viewport(0, 0, (uint)Width, (uint)Height);
    }

    /// <summary>Each shadow cascade's resolution, and how many there can be (see <see cref="FrameRequest.ShadowCascades"/>).</summary>
    public const int CascadeSize = 2048, MaxCascades = 4;

    GpuTexture? _cascades;
    uint _cascadeFbo;

    /// <summary>The cascades' depth array, one layer each - made the first time a frame asks for cascades (64 MB).</summary>
    public GpuTexture ShadowCascades => _cascades ??= CreateCascadeArray();

    unsafe GpuTexture CreateCascadeArray()
    {
        uint handle = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2DArray, handle);
        _gl.TexImage3D(TextureTarget.Texture2DArray, 0, InternalFormat.DepthComponent32f, CascadeSize, CascadeSize, MaxCascades, 0,
            PixelFormat.DepthComponent, PixelType.Float, null);
        _gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureCompareMode, (int)GLEnum.CompareRefToTexture);
        _gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureCompareFunc, (int)GLEnum.Lequal);
        _gl.SetSampling(TextureTarget.Texture2DArray, GLEnum.Linear, GLEnum.ClampToEdge);
        _ownedFixedSize.Add(handle);
        _cascadeFbo = _gl.GenFramebuffer();
        return new GpuTexture(handle, CascadeSize, CascadeSize);
    }

    /// <summary>Binds one cascade's layer as the depth target.</summary>
    public void BindShadowCascadeTarget(int cascade)
    {
        var array = ShadowCascades;
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _cascadeFbo);
        _gl.FramebufferTextureLayer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, array.Handle, 0, cascade);
        _gl.DrawBuffer(DrawBufferMode.None);
        _gl.ReadBuffer(ReadBufferMode.None);
        _gl.Viewport(0, 0, CascadeSize, CascadeSize);
    }

    public void BindShadowTarget()
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _shadowFbo);
        _gl.Viewport(0, 0, ShadowMapSize, ShadowMapSize);
    }

    // Reports an incomplete framebuffer when it is built, naming it. Checked once per construction because the status query can
    // force a driver sync. An incomplete framebuffer discards every draw and clear into it.
    static void ValidateFramebuffer(GL gl, string name)
    {
        var status = gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != GLEnum.FramebufferComplete)
            Console.WriteLine($"[RenderTargets] {name} framebuffer is INCOMPLETE ({status}) - every draw into it will be discarded.");
    }

    GpuTexture CreateColorTexture(int width, int height, InternalFormat format, bool repeat = true, bool filterNearest = false)
    {
        uint handle = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, handle);
        var (pixelFormat, pixelType) = format == InternalFormat.Rgba8
            ? (PixelFormat.Rgba, PixelType.UnsignedByte)
            : (PixelFormat.Rgba, PixelType.Float);
        unsafe { _gl.TexImage2D(TextureTarget.Texture2D, 0, format, (uint)width, (uint)height, 0, pixelFormat, pixelType, null); }
        int filter = (int)(filterNearest ? GLEnum.Nearest : GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, filter);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, filter);
        int wrap = (int)(repeat ? GLEnum.Repeat : GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, wrap);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, wrap);
        _owned.Add(handle);
        return new GpuTexture(handle, width, height);
    }

    unsafe GpuTexture CreateColorTextureArray(int width, int height, InternalFormat format, int layers)
    {
        uint handle = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2DArray, handle);
        _gl.TexImage3D(TextureTarget.Texture2DArray, 0, format, (uint)width, (uint)height, (uint)layers, 0, PixelFormat.Rgba, PixelType.Float, null);
        _gl.SetSampling(TextureTarget.Texture2DArray, GLEnum.Linear, GLEnum.ClampToEdge);
        _owned.Add(handle);
        return new GpuTexture(handle, width, height);
    }

    GpuTexture CreateDepthTexture(int width, int height, bool resizeOwned = true, bool isShadowMap = false)
    {
        uint handle = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, handle);
        unsafe { _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.DepthComponent32f, (uint)width, (uint)height, 0, PixelFormat.DepthComponent, PixelType.Float, null); }
        if (isShadowMap)
        {
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureCompareMode, (int)GLEnum.CompareRefToTexture);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureCompareFunc, (int)GLEnum.Lequal);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        }
        else
        {
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
            _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
        }
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
        (resizeOwned ? _owned : _ownedFixedSize).Add(handle);
        return new GpuTexture(handle, width, height);
    }

    public void Dispose()
    {
        foreach (uint t in _owned)
            _gl.DeleteTexture(t);
        foreach (uint t in _ownedFixedSize)
            _gl.DeleteTexture(t);
        _gl.DeleteFramebuffer(_gbufferFbo);
        _gl.DeleteFramebuffer(_passIdFbo);
        _gl.DeleteFramebuffer(_shadowFbo);
        if (_cascadeFbo != 0)
            _gl.DeleteFramebuffer(_cascadeFbo);
        _gl.DeleteFramebuffer(_scratchColorFbo);
        _gl.DeleteFramebuffer(_scratchColorDepthFbo);
    }
}
