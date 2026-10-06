using Silk.NET.OpenGL;

namespace WildRenderingSharp.Pipeline;

public readonly record struct GpuTexture(uint Handle, int Width, int Height);

/// <summary>
/// Every size-dependent render target the deferred pipeline uses, plus the (few) fixed-size ones
/// (the shadow map). Rebuilt whenever the viewport resizes - mirrors <c>viewer.Viewer._make_targets</c>.
///
/// Framebuffers themselves are NOT one-per-texture-combination like the Python bench's tuple-keyed
/// cache (which keeps every distinct combination it has ever seen alive for the whole session);
/// this instead keeps two small scratch framebuffer objects and repoints their attachment with
/// <c>glFramebufferTexture2D</c> for whichever texture a pass currently needs - a single cheap
/// call, and no unbounded framebuffer-object growth.
/// </summary>
public sealed class RenderTargets : IDisposable
{
    readonly GL _gl;

    public int Width { get; private set; }
    public int Height { get; private set; }

    // ---- G-buffer (persistent, multi-attachment) ----
    public GpuTexture[] GBuffer { get; private set; } = [];
    public GpuTexture GBufferDepth { get; private set; }
    uint _gbufferFbo;

    // ---- linear depth ----
    public GpuTexture LinearDepth { get; private set; }
    public GpuTexture LinearDepthHalf { get; private set; }

    // ---- shadow + SSAO scratch ----
    public GpuTexture PreShadow { get; private set; }
    public GpuTexture PreShadowHalf0 { get; private set; }
    public GpuTexture PreShadowHalf1 { get; private set; }
    public GpuTexture PreMisc { get; private set; }
    public GpuTexture AoRaw { get; private set; }
    public GpuTexture AoTmp { get; private set; }

    /// <summary>
    /// Real per-pixel lighting for <c>cTex_DeferredLightPrePass</c> (binding 28), a
    /// <c>sampler2DArray</c> every <c>chara_*</c> deferred resolve shader samples at layer 0 for
    /// its own main light colour (converted to luminance immediately after - see
    /// <see cref="LightPrePass"/>'s own remarks). Was a flat (0,0,0,0) constant before - WildRenderingSharp fed
    /// this central light-accumulation buffer literally zero light on every material that reads
    /// it, which is very likely the dominant reason the whole pipeline needed a blanket 10x
    /// exposure crutch in the first place. 2 layers to match the sampler's declared array size;
    /// only layer 0 has a confirmed reader across every checked chara_* program, so layer 1 is
    /// left at its zero-initialised default.
    /// </summary>
    public GpuTexture LightPrePassArray { get; private set; }

    // ---- pass-ID mask (persistent, color+depth) ----
    public GpuTexture PassId { get; private set; }
    public GpuTexture PassIdDepth { get; private set; }
    uint _passIdFbo;

    // ---- resolve / tonemap chain ----
    public GpuTexture ResolvePass { get; private set; }
    public GpuTexture Final { get; private set; }
    public GpuTexture Exposed { get; private set; }
    public GpuTexture Compressed { get; private set; }
    public GpuTexture Bloom { get; private set; }
    public GpuTexture Ldr { get; private set; }

    // ---- forward-pass scratch ----
    public GpuTexture Scene { get; private set; }
    public GpuTexture Behind { get; private set; }

    /// <summary>A copy of G-buffer attachment 0 (<c>cTex_GBuffMaterialID</c>) for shapes drawn over the opaque G-buffer - see <see cref="SceneColorShapePass"/>.</summary>
    public GpuTexture MaterialIdCopy { get; private set; }

    // ---- bloom pyramid (4 levels, halved resolution per level) ----
    public GpuTexture[] BloomLevels { get; private set; } = [];
    public GpuTexture[] BloomTmp { get; private set; } = [];
    public const int BloomLevelCount = 4;

    // ---- shadow map (fixed size, independent of viewport) ----
    public const int ShadowMapSize = 4096;
    public GpuTexture ShadowMap { get; }
    uint _shadowFbo;

    /// <summary>Textures owned by the CURRENT viewport size - deleted and rebuilt on every <see cref="Resize"/>.</summary>
    readonly List<uint> _owned = [];
    /// <summary>
    /// Textures whose size does not depend on the viewport and must therefore SURVIVE a resize -
    /// the shadow map, which is a fixed 2048 square. Keeping it in <see cref="_owned"/> meant the
    /// constructor's own call to <see cref="Resize"/> deleted it moments after creating it, leaving
    /// the shadow framebuffer with a dangling attachment: incomplete, so every shadow draw and even
    /// the depth clear was silently discarded, and every consumer of <c>cTex_PreShadow</c> has been
    /// sampling a deleted texture.
    /// </summary>
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
        // A depth-only framebuffer MUST be told it has no colour output. A framebuffer object's
        // default draw buffer is COLOR_ATTACHMENT0, and naming an attachment point that has no
        // image makes the framebuffer INCOMPLETE_DRAW_BUFFER - at which point every draw and every
        // clear into it raises GL_INVALID_FRAMEBUFFER_OPERATION and does nothing at all. Without
        // these two calls the shadow map was never written: not by the shapes, and not even by the
        // depth clear, so it held whatever its texture was created with.
        gl.DrawBuffer(DrawBufferMode.None);
        gl.ReadBuffer(ReadBufferMode.None);
        ValidateFramebuffer(gl, "shadow map");

        Resize(width, height);

        // Checked again after the first Resize, not only at attach time: the shadow map is
        // fixed-size and outlives a resize, so this is where "still complete once the viewport
        // targets exist" is actually established.
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
        if (_gbufferFbo != 0) _gl.DeleteFramebuffer(_gbufferFbo);
        if (_passIdFbo != 0) _gl.DeleteFramebuffer(_passIdFbo);

        // Attachments 0/1/2/3/4 are 8-bit UNORM: the deferred resolve decodes packed flag bits out
        // of the normal target and the albedo alpha with trunc(v * 255), which only holds together
        // at exactly 8-bit quantisation. Attachment 5 is emission and must be half-float - at
        // 8 bits it clamps to 1.0 and can never cross the bloom threshold.
        //
        // filterNearest: true for 0-4 - discrete, bit-packed data (same reasoning PassId below
        // already gets right) must never be linearly interpolated between texels; blending two
        // different packed-bit patterns together produces a nonsense pattern, not a valid one of
        // its own. Any sampling of these attachments that doesn't land exactly on a texel centre
        // (a supersampled render's own resolve step, a screen-space effect with jittered/half-res
        // sampling, etc.) would silently corrupt whichever flags happen to live in the blended
        // bits - a real, plausible source of sharp, resolution-dependent artifacts that a
        // same-resolution-only fullscreen pass would never surface. Previously missing here
        // (defaulted to LINEAR) while already fixed for PassId's own discrete data below.
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

        // These two textures are scalar everywhere they are produced and consumed.  R32F keeps
        // exactly the same 32-bit depth precision while cutting their storage/bandwidth to 1/4.
        LinearDepth = CreateColorTexture(width, height, InternalFormat.R32f);
        LinearDepthHalf = CreateColorTexture(width / 2, height / 2, InternalFormat.R32f);

        // The remaining post-process buffers are ordinary HDR render targets. RGBA16F is the
        // standard lossless-for-display representation here: it retains values up to 65504 and
        // far more precision than the final 8-bit presentation, while halving both VRAM traffic
        // and footprint versus RGBA32F. The real material G-buffer formats above stay untouched.
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
        // Clamped: it is sampled as cTex_ColorBuffer at refracted, normal-offset positions that
        // can land past the edge, where wrapping would show the opposite side of the screen.
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

    /// <summary>Retargets the shared single-color-attachment scratch framebuffer to <paramref name="target"/> and sets the viewport to its size.</summary>
    public void BindColorTarget(GpuTexture target)
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _scratchColorFbo);
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, target.Handle, 0);
        _gl.Viewport(0, 0, (uint)target.Width, (uint)target.Height);
    }

    /// <summary>Retargets the shared color+depth scratch framebuffer - for the forward pass, which depth-tests against the G-buffer depth it was rasterised alongside.</summary>
    public void BindColorAndDepthTarget(GpuTexture color, GpuTexture depth)
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _scratchColorDepthFbo);
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, color.Handle, 0);
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, TextureTarget.Texture2D, depth.Handle, 0);
        _gl.Viewport(0, 0, (uint)color.Width, (uint)color.Height);
    }

    /// <summary>Retargets the shared single-color-attachment scratch framebuffer to one LAYER of a texture array (e.g. <see cref="LightPrePassArray"/>) via <c>glFramebufferTextureLayer</c>, rather than a whole <c>Texture2D</c>.</summary>
    public void BindColorTargetLayer(GpuTexture arrayTarget, int layer)
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _scratchColorFbo);
        _gl.FramebufferTextureLayer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, arrayTarget.Handle, 0, layer);
        _gl.Viewport(0, 0, (uint)arrayTarget.Width, (uint)arrayTarget.Height);
    }

    /// <summary>
    /// Reads back the exact RGBA float value of one pixel from <paramref name="target"/> - used by
    /// the Scene panel's numeric probe to answer "what is this shader value REALLY doing here",
    /// which a screenshot can't: everything on screen has already been through 10x exposure and the
    /// tonemap curve, so a value that's merely "somewhat bright" and one that's "extremely bright"
    /// both just look like flat white - only reading the actual float back distinguishes them, and
    /// only tells the true story when read from a target like <c>Final</c> that sits BEFORE that
    /// grading, which is why this takes an explicit target rather than always reading the display.
    /// <paramref name="x"/>/<paramref name="y"/> are in the target's own bottom-left-origin GL pixel
    /// space, not window/ImGui coordinates - the caller converts.
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
    /// Reads back a whole RGBA8 target as top-down rows (GL itself is bottom-left origin, flipped
    /// here so the caller - the Inspector's Render tab, saving PNGs - never has to think about GL's
    /// convention) - the bulk counterpart to <see cref="ReadPixel"/>'s single-pixel probe read.
    /// Only meaningful for an already-tonemapped 8-bit target like <c>Ldr</c>; an HDR target would
    /// need <c>PixelType.Float</c> and its own caller-side tonemap, neither of which this does.
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
    /// Reads back a whole RGBA32F target as top-down rows of raw linear floats (no clamping, no
    /// tonemap) - the HDR counterpart to <see cref="ReadPixelsRgba8"/>, used by the Inspector's
    /// Render tab "HDR" export. Deliberately reads a scene-referred target like <c>Final</c>
    /// (real linear radiance, BEFORE exposure/tonemap - the same buffer the numeric probe reads),
    /// not an already-exposed one: a genuine HDR file is meant to carry the scene's real dynamic
    /// range for whatever tool opens it to expose/tonemap itself, the same way a camera RAW/HDR
    /// capture does, rather than baking in this app's own exposure setting.
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
        _gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
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

    /// <summary>
    /// Reports an incomplete framebuffer at the moment it is built, naming it. Checked once per
    /// construction rather than per bind (the status query can force a driver sync), which is
    /// enough: these framebuffers' attachments only change when they are rebuilt. An incomplete
    /// framebuffer is not a warning - every draw and clear into it is discarded and raises
    /// GL_INVALID_FRAMEBUFFER_OPERATION, so the pass simply does not happen.
    /// </summary>
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
        _gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
        _owned.Add(handle);
        return new GpuTexture(handle, width, height);
    }

    /// <param name="resizeOwned">Whether this texture belongs to the current viewport size and should be destroyed on the next <see cref="Resize"/>. False for fixed-size targets, which outlive it.</param>
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
