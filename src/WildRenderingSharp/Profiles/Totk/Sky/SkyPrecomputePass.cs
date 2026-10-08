using System.Numerics;
using WildRenderingSharp.Rendering;
using Silk.NET.OpenGL;
using WildRenderingSharp.Pipeline;
using WildRenderingSharp.Profiles.Totk.Shaders;
using WildRenderingSharp.Graphics;

namespace WildRenderingSharp.Profiles.Totk.Sky;

/// <summary>
/// Runs the <c>agl::pfx::Sky</c> precompute chain (Bruneton's multiple-scattering solve), whose end
/// product is the <c>cTexBakedInscatter</c> table that <c>agl_sky_postfx_sky</c> only samples.
/// See <c>docs/agl_sky_postfx.md</c> for the extraction and the evidence for each layout fact.
/// </summary>
/// <remarks>
/// <para>
/// All eleven programs are the game's own. The pass order is the standard multiple-scattering
/// iteration; each program's <c>LOCAL_STEP</c> macro names which iteration of the solve it is.
/// </para>
/// <para>
/// Both uniform blocks are opaque blobs in the binary, so their layouts come from elsewhere.
/// <c>SizeInfo</c> (144 B) was read from a real capture. <c>Config</c> (48 B) holds
/// <c>(betaR.rgb, betaM)</c> and <c>(HR, HM)</c> from <c>master_field.baglsky</c>, plus Bruneton's
/// <c>dhdH</c> per altitude slice (see <see cref="BuildConfig"/>). <c>RenderInfo</c> (128 B) holds
/// the normalised layer at <c>[3].w</c>, from <c>sky_copy_inscatter</c>'s vertex shader.
/// </para>
/// <para>
/// The 3D tables are written one R slice at a time with <c>Config</c> rebuilt for that altitude: the
/// fragment shaders read only <c>gl_FragCoord.xy</c>, so there is no layered rendering. The vertex
/// stages expect a half-unit quad, and every program divides by <c>support_buffer.render_scale[0]</c>,
/// so <see cref="SupportBufferUbo"/> must be bound.
/// </para>
/// </remarks>
public sealed class SkyPrecomputePass : IDisposable
{
    // SizeInfo was read from the game's bound constant buffer during a sky bake draw, found by
    // searching every draw for a vec4 of shape (a, b, 1/a, 1/b); the block cannot be identified by
    // size because the emulator declares every block as data[4096]. All nine slots:
    //   [0] 256, 64,  1/256, 1/64      [4] 32, 1/32, 1/31, 1/15
    //   [1] 64,  64,  1/64,  1/64      [5] 32, 1/32, 1/31, 1/15
    //   [2] 256, 256, 1/256, 1/256     [6] 8,  1/8,  1/7,  1/3
    //   [3] 64,  64,  1/64,  1/64      [7] 16, 1/16, 1/15, 1/7
    //   [8] 6360, 6420, 0, 0
    // The transmittance size and Rg/Rt derived independently from the shader maths matched exactly.
    public const int TransmittanceW = 256;
    public const int TransmittanceH = 64;
    public const int IrradianceW = 64;
    public const int IrradianceH = 64;
    public const int BakedInscatterW = 256;
    public const int BakedInscatterH = 256;
    public const int RangeTransmittanceW = 64;
    public const int RangeTransmittanceH = 64;

    // The four inscatter axes. TotK uses a coarser table than Bruneton's reference (R=32, MU=128,
    // MU_S=32, NU=8): SizeInfo, sky_copy_inscatter's packed width (data[5].x * data[6].x) and the
    // captured texture (256 x 32 x 16) all agree.
    public const int ResMu = 32;    // slot 4 - the packed face's height
    public const int ResMuS = 32;   // slot 5
    public const int ResNu = 8;     // slot 6 - ResMuS * ResNu = 256, the packed width
    public const int ResR = 16;     // slot 7 - depth; the axis the per-slice layer index walks

    public const int InscatterW = ResMuS * ResNu;   // 256
    public const int InscatterH = ResMu;            // 32
    public const int InscatterD = ResR;             // 16

    /// <summary>Ground (planet) radius, from the capture; the ROM's scale heights are in kilometres to match.</summary>
    public const float Rg = 6360f;
    /// <summary>Top-of-atmosphere radius; <c>Rt - Rg</c> is 60 km.</summary>
    public const float Rt = 6420f;

    /// <summary>
    /// Multiple-scattering orders to solve. 6 was chosen by measurement: against the game's captured
    /// inscatter table the mean per-slice correlation was 0.9016, 0.9392, 0.9492 and 0.9523 at 2, 4, 6
    /// and 8 orders, so the curve flattens at 6. Override with <c>WRS_SKY_ORDERS</c> (or the older
    /// <c>MARROW_SKY_ORDERS</c>).
    /// </summary>
    public int ScatteringOrders { get; set; } =
        int.TryParse((Environment.GetEnvironmentVariable("WRS_SKY_ORDERS") ?? Environment.GetEnvironmentVariable("MARROW_SKY_ORDERS")), out int o) && o >= 1 ? o : 6;

    // The decompiled per-stage console indices collide once both stages share one GL program, so
    // each block is rebound explicitly.
    const uint SizeInfoBinding = 22;
    const uint ConfigBinding = 23;
    const uint RenderInfoBinding = 24;

    readonly GL _gl;
    readonly Dictionary<string, uint> _programs = [];
    readonly uint _fbo;
    readonly uint _vao, _vbo;

    uint _transmittance, _deltaE, _irradiance;
    uint _deltaSR, _deltaSM, _deltaJ, _inscatter;
    uint _bakedInscatter;

    /// <summary>The final baked inscatter LUT the real per-frame sky shader samples, or 0 if unavailable.</summary>
    public uint BakedInscatter => _bakedInscatter;
    /// <summary>The transmittance LUT, also sampled directly by the sun-disc sky variant.</summary>
    public uint Transmittance => _transmittance;

    public bool Available { get; }
    Vector3 _sunWorld = new(0f, 0f, 1f);
    float _rayleighAmp = 1f, _mieAmp = 1f;

    // Every program in the chain. Sampler units follow the fp_t_tcb_<hex> naming, where hex is 8 + 2 *
    // the archive's sampler location; that is not the archive's list order, so binding by position swaps textures.
    static readonly string[] ProgramNames =
    [
        "agl_sky_transmittance_step0",
        "agl_sky_irradiance_step1", "agl_sky_irradiance_step2", "agl_sky_irradiance_step3",
        "agl_sky_inscatter_step1", "agl_sky_inscatter_step2",
        "agl_sky_delta_inscatter_step2", "agl_sky_delta_inscatter_step3",
        "agl_sky_copy_inscatter_step1", "agl_sky_copy_inscatter_step2",
        "agl_sky_copy_irradiance_step0",
        "agl_sky_bake_inscatter",
    ];

    // The copy and bake passes take RenderInfo at c4; every solve pass takes Config there.
    static readonly HashSet<string> UsesRenderInfo =
    [
        "agl_sky_copy_inscatter_step1", "agl_sky_copy_inscatter_step2",
        "agl_sky_copy_irradiance_step0", "agl_sky_bake_inscatter",
    ];

    public unsafe SkyPrecomputePass(GL gl, ShaderProgramCache programs)
    {
        _gl = gl;

        foreach (string name in ProgramNames)
        {
            if (!programs.Exists(name))
            {
                Console.WriteLine($"[SkyPrecomputePass] '{name}' missing from the shader cache - " +
                    "sky precompute disabled (run ModelPreparer.EnsureSkyShaders with a romfs path set).");
                return;
            }
        }

        foreach (string name in ProgramNames)
        {
            uint prog = programs.Load(name);
            _programs[name] = prog;

            uint c4 = UsesRenderInfo.Contains(name) ? RenderInfoBinding : ConfigBinding;
            _gl.BindUniformBlock(prog, "_fp_c3", SizeInfoBinding);
            _gl.BindUniformBlock(prog, "_vp_c3", SizeInfoBinding);
            _gl.BindUniformBlock(prog, "_fp_c4", c4);
            _gl.BindUniformBlock(prog, "_vp_c4", c4);

            _gl.UseProgram(prog);
            AssignSamplerUnits(prog, name);
        }

        Vector4[] quad =
        [
            new Vector4(-0.5f, -0.5f, 0f, 1f),
            new Vector4( 0.5f, -0.5f, 0f, 1f),
            new Vector4(-0.5f,  0.5f, 0f, 1f),
            new Vector4( 0.5f,  0.5f, 0f, 1f),
        ];
        _vao = gl.GenVertexArray();
        _vbo = gl.GenBuffer();
        gl.BindVertexArray(_vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        fixed (Vector4* p = quad)
            gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(quad.Length * sizeof(Vector4)), p, BufferUsageARB.StaticDraw);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 4, VertexAttribPointerType.Float, false, (uint)sizeof(Vector4), (void*)0);
        gl.BindVertexArray(0);

        _transmittance = CreateLut2D(gl, TransmittanceW, TransmittanceH);
        _deltaE = CreateLut2D(gl, IrradianceW, IrradianceH);
        _irradiance = CreateLut2D(gl, IrradianceW, IrradianceH);
        _bakedInscatter = CreateLut2D(gl, BakedInscatterW, BakedInscatterH);
        _deltaSR = CreateLut3D(gl, InscatterW, InscatterH, InscatterD);
        _deltaSM = CreateLut3D(gl, InscatterW, InscatterH, InscatterD);
        _deltaJ = CreateLut3D(gl, InscatterW, InscatterH, InscatterD);
        _inscatter = CreateLut3D(gl, InscatterW, InscatterH, InscatterD);

        _fbo = gl.GenFramebuffer();
        Available = true;
        Console.WriteLine($"[SkyPrecomputePass] linked all {ProgramNames.Length} real agl sky programs; " +
            $"inscatter LUT {InscatterW}x{InscatterH}x{InscatterD}, transmittance {TransmittanceW}x{TransmittanceH}.");
    }

    /// <summary>Points each program's samplers at fixed texture units, following the shader's own <c>fp_t_tcb_&lt;hex&gt;</c> naming.</summary>
    void AssignSamplerUnits(uint prog, string name)
    {
        switch (name)
        {
            case "agl_sky_irradiance_step1":            // cTexTransmittance
            case "agl_sky_inscatter_step1":             // cTexTransmittance
            case "agl_sky_irradiance_step3":            // cTexDeltaSR
            case "agl_sky_copy_irradiance_step0":       // cTexDeltaE
            case "agl_sky_bake_inscatter":              // cTexInscatter
                _gl.SetSamplerUnit(prog, "fp_t_tcb_8", 0);
                break;

            case "agl_sky_irradiance_step2":            // SR@0, SM@1
            case "agl_sky_copy_inscatter_step1":        // SR@0, SM@1
            case "agl_sky_inscatter_step2":             // T@0, deltaJ@1
                _gl.SetSamplerUnit(prog, "fp_t_tcb_8", 0);
                _gl.SetSamplerUnit(prog, "fp_t_tcb_A", 1);
                break;

            case "agl_sky_copy_inscatter_step2":        // cTexDeltaSR only
                _gl.SetSamplerUnit(prog, "fp_t_tcb_8", 0);
                break;

            case "agl_sky_delta_inscatter_step2":       // T@0, E@1, SR@2, SM@3
                _gl.SetSamplerUnit(prog, "fp_t_tcb_8", 0);
                _gl.SetSamplerUnit(prog, "fp_t_tcb_A", 1);
                _gl.SetSamplerUnit(prog, "fp_t_tcb_C", 2);
                _gl.SetSamplerUnit(prog, "fp_t_tcb_E", 3);
                break;

            case "agl_sky_delta_inscatter_step3":       // T@0, E@1, SR@2
                _gl.SetSamplerUnit(prog, "fp_t_tcb_8", 0);
                _gl.SetSamplerUnit(prog, "fp_t_tcb_A", 1);
                _gl.SetSamplerUnit(prog, "fp_t_tcb_C", 2);
                break;
        }
    }

    static unsafe uint CreateLut2D(GL gl, int w, int h)
    {
        uint tex = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, tex);
        // 16F as in the game's allocation; transmittance spans orders of magnitude along a grazing ray.
        gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba16f, (uint)w, (uint)h, 0,
            PixelFormat.Rgba, PixelType.Float, null);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        // Clamp: wrapping an edge would fold a grazing ray onto a zenith ray.
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
        return tex;
    }

    static unsafe uint CreateLut3D(GL gl, int w, int h, int d)
    {
        uint tex = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture3D, tex);
        gl.TexImage3D(TextureTarget.Texture3D, 0, InternalFormat.Rgba16f, (uint)w, (uint)h, (uint)d, 0,
            PixelFormat.Rgba, PixelType.Float, null);
        gl.SetSampling(TextureTarget.Texture3D, GLEnum.Linear, GLEnum.ClampToEdge);
        gl.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureWrapR, (int)GLEnum.ClampToEdge);
        return tex;
    }

    /// <summary>The 144-byte <c>SizeInfo</c> blob, reproducing the game's own bound contents exactly.</summary>
    internal static byte[] BuildSizeInfo()
    {
        var buf = new byte[144];
        var u = new UniformWriter(buf);
        void Dims(int slot, int w, int h)
        {
            u.Set(slot, 0, w); u.Set(slot, 1, h);
            u.Set(slot, 2, 1f / w); u.Set(slot, 3, 1f / h);
        }
        // (n, 1/n, 1/(n-1), 1/(n/2 - 1)): texel-centre remap constants for a full and a half-resolution walk.
        void Axis(int slot, int n)
        {
            u.Set(slot, 0, n);
            u.Set(slot, 1, 1f / n);
            u.Set(slot, 2, 1f / (n - 1));
            u.Set(slot, 3, 1f / (n / 2 - 1));
        }

        Dims(0, TransmittanceW, TransmittanceH);
        Dims(1, IrradianceW, IrradianceH);
        Dims(2, BakedInscatterW, BakedInscatterH);
        Dims(3, RangeTransmittanceW, RangeTransmittanceH);
        Axis(4, ResMu);
        Axis(5, ResMuS);
        Axis(6, ResNu);
        Axis(7, ResR);
        u.Set(8, 0, Rg);
        u.Set(8, 1, Rt);
        return buf;
    }

    /// <summary>
    /// The <c>RenderInfo</c> the game binds to <c>sky_bake_inscatter</c>, as captured. Known slots are
    /// overwritten by <see cref="BuildBakeRenderInfo"/>; the rest are kept because zeroing them is
    /// demonstrably wrong.
    /// </summary>
    /// <remarks>
    /// This pass is 2D over the whole table, so it has no layer. Its <c>[0]</c> carries
    /// <c>betaR</c>/<c>betaM</c>, the weights that turn the Rayleigh and Mie tables into colour: with
    /// them zero the table has a healthy alpha and zero RGB, which is a black sky.
    /// </remarks>
    static readonly float[] BakeRenderInfoBaseline =
    [
        0.00411f, 0.011275f, 0.028364f, 0.0018f,     // [0] betaR.rgb, betaM      (overwritten)
        0.75f, 1f, 12f, 0f,                          // [1] unidentified
        0.0092173358f, 0.67337108f, 0.73924726f, 0f, // [2] sun direction, Y-up   (overwritten)
        18f, 16.588242f, 15.3f, 0.0039553195f,       // [3] unidentified
        59.999023f, 879.13525f, 0.0009765625f, 3.4641016f, // [4] unidentified; .x is Rt-Rg
        0.95547581f, 25000f, 24999.045f, 0.040001526f,     // [5] unidentified; range-ish
        0.5f, 0.4f, 0.3f, 1f,                        // [6] GroundColor, .w=1     (overwritten)
        0f, 0f, 0f, 0f,                              // [7] zero in the capture
    ];

    /// <summary>The 128-byte <c>RenderInfo</c> for <c>sky_bake_inscatter</c>.</summary>
    internal static byte[] BuildBakeRenderInfo(SkyPostFx postfx, Vector3 sunWorldZUp)
    {
        var buf = new byte[128];
        for (int i = 0; i < BakeRenderInfoBaseline.Length && i * 4 + 4 <= buf.Length; i++)
            BitConverter.GetBytes(BakeRenderInfoBaseline[i]).CopyTo(buf, i * 4);

        var u = new UniformWriter(buf);

        var br = postfx.RayleighScatteringCoeff;
        u.Set(0, 0, br.X); u.Set(0, 1, br.Y); u.Set(0, 2, br.Z); u.Set(0, 3, postfx.MieScatteringCoeff);

        // Y-up, like every other agl sky and cloud input.
        Vector3 sun = new(sunWorldZUp.X, sunWorldZUp.Z, sunWorldZUp.Y);
        if (sun.LengthSquared() > 1e-12f) sun = Vector3.Normalize(sun);
        u.Set(2, 0, sun.X); u.Set(2, 1, sun.Y); u.Set(2, 2, sun.Z);

        var g = postfx.GroundColor;
        u.Set(6, 0, g.X); u.Set(6, 1, g.Y); u.Set(6, 2, g.Z); u.Set(6, 3, 1f);
        return buf;
    }

    /// <summary>
    /// The 48-byte <c>Config</c> blob: the ROM's atmosphere parameters plus Bruneton's <c>dhdH</c> for
    /// one altitude slice.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>[2] = dhdH = (dmin, dmax, dminp, dmaxp)</c> is inferred. The shaders clamp against it in two
    /// pairs, <c>max(t, [2].x)</c> with <c>min(t, [2].y * 0.999)</c> and the same for <c>.z/.w</c>, which is
    /// how Bruneton clamps the sky and ground branches, and <c>inscatter_step1</c> recovers the radius as
    /// <c>[2].z + Rg</c> (dminp is defined as <c>r - Rg</c>).
    /// </para>
    /// <para>
    /// <c>[1].x</c> is read but unidentified, and left zero. Only the multiple-scattering source terms
    /// read it, which leaves the Mie asymmetry or the ground reflectance; feeding the asymmetry (0.85)
    /// slightly worsened correlation with the captured table (0.9392 to 0.9383), so it is not that. It is
    /// the leading suspect for a residual blue deficit of about 20%. <c>[1].w</c> is unread.
    /// </para>
    /// </remarks>
    internal static byte[] BuildConfig(SkyPostFx postfx, int layer,
        float rayleighAmplifier = 1f, float mieAmplifier = 1f)
    {
        var buf = new byte[48];
        var u = new UniformWriter(buf);

        // The palette's amplifiers scale the coefficients, which is what changes the sky's colour per palette (a red moon, a storm).
        var br = postfx.RayleighScatteringCoeff * rayleighAmplifier;
        u.Set(0, 0, br.X);
        u.Set(0, 1, br.Y);
        u.Set(0, 2, br.Z);
        u.Set(0, 3, postfx.MieScatteringCoeff * mieAmplifier);
        u.Set(1, 1, postfx.RayleighBaseHeight);
        u.Set(1, 2, postfx.MieBaseHeight);

        var (dmin, dmax, dminp, dmaxp) = SliceGeometry(layer);
        u.Set(2, 0, dmin);
        u.Set(2, 1, dmax);
        u.Set(2, 2, dminp);
        u.Set(2, 3, dmaxp);
        return buf;
    }

    /// <summary>Bruneton's per-layer radius and the four ray-length bounds derived from it.</summary>
    /// <remarks>
    /// The epsilon nudges at the first and last layer are load-bearing: exactly at <c>r == Rg</c> the
    /// horizon term is 0 and exactly at <c>r == Rt</c> the sky-branch length collapses, putting a
    /// division on a knife edge and producing NaN across the slice.
    /// </remarks>
    internal static (float Dmin, float Dmax, float Dminp, float Dmaxp) SliceGeometry(int layer)
    {
        float t = layer / (float)(ResR - 1);
        t *= t;
        float r = MathF.Sqrt(Rg * Rg + t * (Rt * Rt - Rg * Rg))
                  + (layer == 0 ? 0.01f : layer == ResR - 1 ? -0.001f : 0f);

        float horizon = MathF.Sqrt(MathF.Max(0f, r * r - Rg * Rg));
        float topHorizon = MathF.Sqrt(MathF.Max(0f, Rt * Rt - Rg * Rg));
        return (Rt - r, horizon + topHorizon, r - Rg, horizon);
    }

    /// <summary>The 128-byte <c>RenderInfo</c> blob; <c>[3].w</c> is the normalised layer.</summary>
    /// <remarks>
    /// <c>sky_copy_inscatter</c>'s vertex shader computes <c>L * (ResR - 1) + 0.5</c>, so landing on
    /// slice <c>i</c>'s texel centre needs <c>L = i / (ResR - 1)</c>.
    /// </remarks>
    internal static byte[] BuildRenderInfo(int layer)
    {
        var buf = new byte[128];
        BitConverter.GetBytes(layer / (float)(ResR - 1)).CopyTo(buf, 3 * 16 + 3 * 4);
        return buf;
    }

    void Target2D(uint tex, int w, int h)
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, tex, 0);
        DrawBuffers(1);
        _gl.Viewport(0, 0, (uint)w, (uint)h);
    }

    void TargetLayer(uint tex, int layer)
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
        _gl.FramebufferTextureLayer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
            tex, 0, layer);
        DrawBuffers(1);
        _gl.Viewport(0, 0, InscatterW, InscatterH);
    }

    void TargetLayer2(uint tex0, uint tex1, int layer)
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _fbo);
        _gl.FramebufferTextureLayer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, tex0, 0, layer);
        _gl.FramebufferTextureLayer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment1, tex1, 0, layer);
        DrawBuffers(2);
        _gl.Viewport(0, 0, InscatterW, InscatterH);
    }

    unsafe void DrawBuffers(int count)
    {
        Span<GLEnum> bufs = [GLEnum.ColorAttachment0, GLEnum.ColorAttachment1];
        fixed (GLEnum* p = bufs)
            _gl.DrawBuffers((uint)count, p);
        if (count < 2)
            _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment1,
                TextureTarget.Texture2D, 0, 0);
    }

    void Bind(int unit, TextureTarget target, uint tex)
    {
        _gl.ActiveTexture(TextureUnit.Texture0 + unit);
        _gl.BindTexture(target, tex);
    }

    void DrawQuad(string program)
    {
        _gl.UseProgram(_programs[program]);
        _gl.BindVertexArray(_vao);
        _gl.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
        _gl.BindVertexArray(0);
    }

    void ClearTarget()
    {
        _gl.ClearColor(0f, 0f, 0f, 0f);
        _gl.Clear(ClearBufferMask.ColorBufferBit);
    }

    /// <summary>Runs the whole precompute. The caller guards against repeating it.</summary>
    /// <param name="sunWorldZUp">The Z-up sun direction, converted internally for the bake pass.</param>
    public void Run(GLResourceCache resources, SkyPostFx postfx, Vector3 sunWorldZUp = default,
        float rayleighAmplifier = 1f, float mieAmplifier = 1f, Vector3? paletteTint = null)
    {
        PaletteTint = paletteTint ?? Vector3.One;
        _rayleighAmp = rayleighAmplifier;
        _mieAmp = mieAmplifier;
        _sunWorld = sunWorldZUp == default ? new Vector3(0f, 0f, 1f) : sunWorldZUp;
        if (!Available)
            return;

        resources.Ubo("sky_support", SupportBufferUbo.Build(), SupportBufferUbo.BindingIndex);
        resources.Ubo("sky_sizeinfo", BuildSizeInfo(), SizeInfoBinding);

        // The atmosphere inputs are all that distinguishes this run from the game's; a spectral mismatch shows up here first.
        var br = postfx.RayleighScatteringCoeff;
        Console.WriteLine($"[SkyPrecomputePass] atmosphere: betaR=({br.X:G6}, {br.Y:G6}, {br.Z:G6}) " +
            $"betaM={postfx.MieScatteringCoeff:G6} HR={postfx.RayleighBaseHeight:G6} HM={postfx.MieBaseHeight:G6}");

        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.CullFace);
        _gl.Disable(EnableCap.Blend);

        void Config(int layer) => resources.Ubo("sky_config", BuildConfig(postfx, layer, _rayleighAmp, _mieAmp), ConfigBinding);
        void RenderInfo(int layer) => resources.Ubo("sky_renderinfo", BuildRenderInfo(layer), RenderInfoBinding);

        // 1. transmittance T
        Config(0);
        Target2D(_transmittance, TransmittanceW, TransmittanceH);
        DrawQuad("agl_sky_transmittance_step0");

        // 2. ground irradiance from direct sunlight -> deltaE
        Bind(0, TextureTarget.Texture2D, _transmittance);
        Target2D(_deltaE, IrradianceW, IrradianceH);
        DrawQuad("agl_sky_irradiance_step1");

        // E starts at zero: irradiance accumulates from order 2 onward, and the direct term belongs to deltaE only.
        Target2D(_irradiance, IrradianceW, IrradianceH);
        ClearTarget();

        // 3. single-scattering deltaSR + deltaSM, written together (the one MRT pass)
        Bind(0, TextureTarget.Texture2D, _transmittance);
        for (int layer = 0; layer < ResR; layer++)
        {
            Config(layer);
            TargetLayer2(_deltaSR, _deltaSM, layer);
            DrawQuad("agl_sky_inscatter_step1");
        }

        // 4. S = deltaSR + deltaSM
        Bind(0, TextureTarget.Texture3D, _deltaSR);
        Bind(1, TextureTarget.Texture3D, _deltaSM);
        for (int layer = 0; layer < ResR; layer++)
        {
            RenderInfo(layer);
            TargetLayer(_inscatter, layer);
            DrawQuad("agl_sky_copy_inscatter_step1");
        }

        // 5. multiple scattering
        for (int order = 2; order <= ScatteringOrders; order++)
        {
            bool first = order == 2;

            // 5a. deltaJ; the first iteration also folds in the separate Mie delta (the step2/step3 split).
            Bind(0, TextureTarget.Texture2D, _transmittance);
            Bind(1, TextureTarget.Texture2D, _deltaE);
            Bind(2, TextureTarget.Texture3D, _deltaSR);
            if (first)
                Bind(3, TextureTarget.Texture3D, _deltaSM);
            for (int layer = 0; layer < ResR; layer++)
            {
                Config(layer);
                TargetLayer(_deltaJ, layer);
                DrawQuad(first ? "agl_sky_delta_inscatter_step2" : "agl_sky_delta_inscatter_step3");
            }

            // 5b. deltaE for this order
            Bind(0, TextureTarget.Texture3D, _deltaSR);
            if (first)
                Bind(1, TextureTarget.Texture3D, _deltaSM);
            Config(0);
            Target2D(_deltaE, IrradianceW, IrradianceH);
            DrawQuad(first ? "agl_sky_irradiance_step2" : "agl_sky_irradiance_step3");

            // 5c. deltaSR for this order, from deltaJ
            Bind(0, TextureTarget.Texture2D, _transmittance);
            Bind(1, TextureTarget.Texture3D, _deltaJ);
            for (int layer = 0; layer < ResR; layer++)
            {
                Config(layer);
                TargetLayer(_deltaSR, layer);
                DrawQuad("agl_sky_inscatter_step2");
            }

            // 5d/5e. Accumulate into E and S: the copy programs are the += of the iteration.
            _gl.Enable(EnableCap.Blend);
            _gl.BlendFunc(BlendingFactor.One, BlendingFactor.One);
            // Explicit equation: this accumulation is baked into the table, so a leaked subtract would persist until the next bake.
            _gl.BlendEquation(BlendEquationModeEXT.FuncAdd);

            Bind(0, TextureTarget.Texture2D, _deltaE);
            Target2D(_irradiance, IrradianceW, IrradianceH);
            DrawQuad("agl_sky_copy_irradiance_step0");

            Bind(0, TextureTarget.Texture3D, _deltaSR);
            for (int layer = 0; layer < ResR; layer++)
            {
                RenderInfo(layer);
                TargetLayer(_inscatter, layer);
                DrawQuad("agl_sky_copy_inscatter_step2");
            }

            _gl.Disable(EnableCap.Blend);
        }

        // 6. Collapse the 4D table into the 2D table the per-frame sky shader samples. This pass needs
        // the full RenderInfo, which carries betaR/betaM.
        Bind(0, TextureTarget.Texture3D, _inscatter);
        resources.Ubo("sky_bake_renderinfo", BuildBakeRenderInfo(postfx, _sunWorld), RenderInfoBinding);
        Target2D(_bakedInscatter, BakedInscatterW, BakedInscatterH);
        DrawQuad("agl_sky_bake_inscatter");

        ApplySpectralCalibration();

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        _gl.ActiveTexture(TextureUnit.Texture0);
    }

    /// <summary>Per-channel gain applied to the finished table. Neutral.</summary>
    /// <remarks>
    /// A fixed gain once compensated an unexplained residual (see <see cref="BuildConfig"/>,
    /// <c>Config[1].x</c>) measured against a single noon palette. Applied to every palette it dragged
    /// saturated reds toward orange, and the peak normalisation and per-palette tint now do that job.
    /// Kept as a hook.
    /// </remarks>
    public Vector3 SpectralCalibration { get; set; } = Vector3.One;

    /// <summary>Per-palette colour folded into the baked table; 0 is none.</summary>
    /// <remarks>
    /// Multiplied into the finished table rather than applied through the sky shader's blend bias,
    /// which saturates wherever the table's alpha is already 1 and then ignores the palette colour.
    /// </remarks>
    public Vector3 PaletteTint { get; set; } = Vector3.One;

    /// <summary>The peak the baked table is normalised to, so palettes with very different scattering amplifiers land in one range instead of clipping.</summary>
    public const float NormalisedPeak = 60f;

    uint _calibProgram;

    const string CalibVert = """
        #version 330 core
        const vec2 P[4] = vec2[4](vec2(-1,-1), vec2(1,-1), vec2(-1,1), vec2(1,1));
        out vec2 vUV;
        void main() { vUV = P[gl_VertexID] * 0.5 + 0.5; gl_Position = vec4(P[gl_VertexID], 0, 1); }
        """;

    const string CalibFrag = """
        #version 330 core
        in vec2 vUV;
        uniform sampler2D tSrc;
        uniform vec3 uGain;
        out vec4 oCol;
        void main() { vec4 c = texture(tSrc, vUV); oCol = vec4(c.rgb * uGain, c.a); }
        """;

    /// <summary>Multiplies the baked table in place by the calibration, through a scratch copy (a texture cannot be its own target and sampler).</summary>
    void ApplySpectralCalibration()
    {
        // Normalise the peak before tinting: a palette's amplifiers change the table's magnitude
        // enormously (Mie x256 takes the peak from about 60 to 427) and a fixed exposure then clips the
        // channels unevenly, turning deep red to pink or white. The solve gives hue and shape;
        // brightness is left to BgDifIntensity and the atmosphere intensity slider.
        float[] pre = ReadBack2D(_bakedInscatter, BakedInscatterW, BakedInscatterH);
        float peak = 0f;
        for (int i = 0; i < pre.Length; i++)
            if ((i & 3) != 3 && float.IsFinite(pre[i]) && pre[i] > peak)
                peak = pre[i];

        float normalise = peak > 1e-6f ? NormalisedPeak / peak : 1f;
        Vector3 gain = SpectralCalibration * PaletteTint * normalise;
        Console.WriteLine($"[SkyPrecomputePass] LUT peak {peak:G4} -> normalised x{normalise:G4} " +
            $"(tint {PaletteTint.X:F2},{PaletteTint.Y:F2},{PaletteTint.Z:F2})");

        if (_calibProgram == 0)
            _calibProgram = GLProgramBuilder.Build(_gl, CalibVert, CalibFrag, "sky_spectral_calibration");

        uint scratch = CreateLut2D(_gl, BakedInscatterW, BakedInscatterH);
        Target2D(scratch, BakedInscatterW, BakedInscatterH);
        _gl.UseProgram(_calibProgram);
        _gl.BindTextureUniform(_calibProgram, "tSrc", 0, _bakedInscatter);
        _gl.SetVec3(_calibProgram, "uGain", gain);
        _gl.BindVertexArray(_vao);
        _gl.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
        _gl.BindVertexArray(0);

        _gl.DeleteTexture(_bakedInscatter);
        _bakedInscatter = scratch;
    }

    /// <summary>The <c>SizeInfo</c> bytes as read from the game, to check <see cref="BuildSizeInfo"/> reproduces them.</summary>
    static readonly float[] CapturedSizeInfo =
    [
        256f, 64f, 0.00390625f, 0.015625f,
        64f, 64f, 0.015625f, 0.015625f,
        256f, 256f, 0.00390625f, 0.00390625f,
        64f, 64f, 0.015625f, 0.015625f,
        32f, 0.03125f, 0.032258064f, 0.06666667f,
        32f, 0.03125f, 0.032258064f, 0.06666667f,
        8f, 0.125f, 0.14285715f, 0.33333334f,
        16f, 0.0625f, 0.06666667f, 0.14285715f,
        6360f, 6420f, 0f, 0f,
    ];

    /// <summary>Checks the constructed <c>SizeInfo</c> against the captured ground truth.</summary>
    public static (bool Ok, float WorstError, int WorstSlot) VerifySizeInfoAgainstCapture()
    {
        byte[] built = BuildSizeInfo();
        float worst = 0f;
        int worstIdx = -1;
        for (int i = 0; i < CapturedSizeInfo.Length; i++)
        {
            float got = BitConverter.ToSingle(built, i * 4);
            float want = CapturedSizeInfo[i];
            float err = want == 0f ? MathF.Abs(got)
                                   : MathF.Abs(got - want) / MathF.Max(1e-6f, MathF.Abs(want));
            if (err > worst) { worst = err; worstIdx = i; }
        }
        return (worst < 1e-6f, worst, worstIdx / 4);
    }

    unsafe float[] ReadBack2D(uint tex, int w, int h)
    {
        var px = new float[w * h * 4];
        _gl.BindTexture(TextureTarget.Texture2D, tex);
        fixed (float* p = px)
            _gl.GetTexImage(TextureTarget.Texture2D, 0, PixelFormat.Rgba, PixelType.Float, p);
        return px;
    }

    unsafe float[] ReadBack3D(uint tex, int w, int h, int d)
    {
        var px = new float[w * h * d * 4];
        _gl.BindTexture(TextureTarget.Texture3D, tex);
        fixed (float* p = px)
            _gl.GetTexImage(TextureTarget.Texture3D, 0, PixelFormat.Rgba, PixelType.Float, p);
        return px;
    }

    static (float Min, float Max, int NonFinite, int Negative) Stats(float[] px)
    {
        float min = float.MaxValue, max = float.MinValue;
        int bad = 0, neg = 0;
        for (int i = 0; i < px.Length; i++)
        {
            float v = px[i];
            if (!float.IsFinite(v)) { bad++; continue; }
            if (v < -1e-4f) neg++;
            if (v < min) min = v;
            if (v > max) max = v;
        }
        if (bad == px.Length) { min = max = float.NaN; }
        return (min, max, bad, neg);
    }

    /// <summary>
    /// Reports each table's range and finiteness. Every quantity in the chain is a radiance or a
    /// transmittance, so non-finite or negative values mean a wrong dhdH, a mis-bound sampler or a
    /// slice rendered with the wrong Config.
    /// </summary>
    public bool Verify()
    {
        if (!Available)
            return false;

        var (sizeOk, sizeErr, sizeSlot) = VerifySizeInfoAgainstCapture();
        Console.WriteLine($"[SkyPrecomputePass] SizeInfo vs real capture: " +
            $"{(sizeOk ? "EXACT" : $"MISMATCH (worst rel err {sizeErr:E2} at slot {sizeSlot})")}");

        bool ok = sizeOk;
        void Report(string name, float[] px)
        {
            var (min, max, bad, neg) = Stats(px);
            bool good = bad == 0 && neg == 0 && max > 0f;
            ok &= good;
            Console.WriteLine($"[SkyPrecomputePass]   {name,-12} range [{min:E3}, {max:E3}] " +
                $"non-finite={bad} negative={neg} -> {(good ? "ok" : "BAD")}");
        }

        Report("transmittance", ReadBack2D(_transmittance, TransmittanceW, TransmittanceH));
        Report("irradiance", ReadBack2D(_irradiance, IrradianceW, IrradianceH));
        Report("deltaSR", ReadBack3D(_deltaSR, InscatterW, InscatterH, InscatterD));
        Report("deltaSM", ReadBack3D(_deltaSM, InscatterW, InscatterH, InscatterD));
        Report("inscatter", ReadBack3D(_inscatter, InscatterW, InscatterH, InscatterD));
        // RGB and alpha separately: all-zero RGB with a healthy alpha reads as a normal range while rendering black.
        float[] baked = ReadBack2D(_bakedInscatter, BakedInscatterW, BakedInscatterH);
        Report("bakedInscat", baked);
        var rgb = new float[baked.Length / 4 * 3];
        var alpha = new float[baked.Length / 4];
        for (int i = 0, j = 0; i < baked.Length; i += 4, j++)
        {
            rgb[j * 3 + 0] = baked[i]; rgb[j * 3 + 1] = baked[i + 1]; rgb[j * 3 + 2] = baked[i + 2];
            alpha[j] = baked[i + 3];
        }
        var (rmin, rmax, _, _) = Stats(rgb);
        var (amin, amax, _, _) = Stats(alpha);
        Console.WriteLine($"[SkyPrecomputePass]   bakedInscat RGB range [{rmin:E3}, {rmax:E3}], " +
            $"ALPHA range [{amin:E3}, {amax:E3}]");

        // Optionally dumps the inscatter table for diffing against one lifted from a GPU capture (8 MB).
        string? dump = (Environment.GetEnvironmentVariable("WRS_SKY_DUMP") ?? Environment.GetEnvironmentVariable("MARROW_SKY_DUMP"));
        if (!string.IsNullOrEmpty(dump))
        {
            float[] px = ReadBack3D(_inscatter, InscatterW, InscatterH, InscatterD);
            var bytes = new byte[px.Length * 4];
            System.Buffer.BlockCopy(px, 0, bytes, 0, bytes.Length);
            File.WriteAllBytes(dump, bytes);
            Console.WriteLine($"[SkyPrecomputePass] dumped inscatter ({InscatterW}x{InscatterH}x{InscatterD}, " +
                $"float32 RGBA) to {dump}");
        }

        Console.WriteLine($"[SkyPrecomputePass] chain ({ScatteringOrders} scattering orders) -> " +
            $"{(ok ? "PASS" : "FAIL")}");
        return ok;
    }

    public void Dispose()
    {
        foreach (uint t in new[] { _transmittance, _deltaE, _irradiance, _deltaSR, _deltaSM, _deltaJ, _inscatter, _bakedInscatter })
            if (t != 0) _gl.DeleteTexture(t);
        if (_fbo != 0) _gl.DeleteFramebuffer(_fbo);
        if (_vbo != 0) _gl.DeleteBuffer(_vbo);
        if (_vao != 0) _gl.DeleteVertexArray(_vao);
    }
}
