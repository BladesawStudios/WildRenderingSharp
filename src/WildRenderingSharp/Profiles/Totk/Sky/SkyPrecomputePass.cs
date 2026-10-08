using System.Numerics;
using WildRenderingSharp.Rendering;
using Silk.NET.OpenGL;
using WildRenderingSharp.Pipeline;
using WildRenderingSharp.Profiles.Totk.Shaders;

namespace WildRenderingSharp.Profiles.Totk.Sky;

/// <summary>
/// Runs the real <c>agl::pfx::Sky</c> Bruneton precompute chain, whose end product is the
/// <c>cTexBakedInscatter</c> LUT that the real per-frame sky shader (<c>agl_sky_postfx_sky</c>)
/// does nothing but sample. See <c>docs/agl_sky_postfx.md</c> for the extraction, the full decode,
/// and the per-macro evidence behind the extracted variants.
/// </summary>
/// <remarks>
/// <para>
/// All eleven programs are the game's own, decompiled from romfs. Nothing here is a stand-in.
/// The pass order is Bruneton's standard multiple-scattering iteration, which is what the
/// programs' own <c>LOCAL_STEP</c> values enumerate - that macro is not a variant to choose
/// between, it names which iteration of the solve each program is.
/// </para>
/// <para>
/// <b>Layout facts, and where each came from.</b> Both uniform blocks are declared by
/// <c>agl::pfx::Sky::Sky::initialize</c> as opaque BLOBS (type <c>0x13</c>), so unlike
/// <c>gsys_env</c>'s base region there is no field-name table in the binary to recover:
/// <list type="bullet">
/// <item><b><c>SizeInfo</c> (144 B, <c>fp_c3</c>) - READ FROM A REAL CAPTURE</b>, every slot. See
/// the constants below. Texture slots are <c>(w, h, 1/w, 1/h)</c>; axis slots are
/// <c>(n, 1/n, 1/(n-1), 1/(n/2-1))</c>.</item>
/// <item><b><c>Config</c> (48 B, <c>fp_c4</c> on the solve passes)</b> - <c>[0]</c> =
/// <c>(betaR.rgb, betaM)</c> and <c>[1].y/.z</c> = <c>(HR, HM)</c>, all read straight from the
/// ROM's own <c>master_field.baglsky</c> via <see cref="SkyPostFx"/>, so nothing is hardcoded.
/// <c>[2]</c> is Bruneton's <b><c>dhdH = (dmin, dmax, dminp, dmaxp)</c></b>, recomputed per
/// altitude slice - see <see cref="BuildConfig"/> for the evidence, which is an exact algebraic
/// identity rather than a guess.</item>
/// <item><b><c>RenderInfo</c> (128 B, <c>fp_c4</c> on the copy/bake passes)</b> - <c>[3].w</c> is
/// the normalised layer, <c>layer/(ResR-1)</c>. Confirmed from <c>sky_copy_inscatter</c>'s vertex
/// shader, which computes <c>data[7].x * L - L + 0.5</c>, i.e. the texel-centre z of that
/// slice.</item>
/// </list>
/// </para>
/// <para>
/// <b>How the 3D LUTs are written.</b> Slice by slice: the fragment shaders only ever read
/// <c>gl_FragCoord.xy</c> (spanning the packed 256 x ResMu face), and take the altitude from
/// <c>Config[2]</c> - <c>inscatter_step1</c> recovers the radius as <c>Config[2].z + Rg</c>. So
/// each 3D pass is dispatched once per R slice with <c>Config</c> rebuilt for that altitude, and
/// the target attached with <c>glFramebufferTextureLayer</c>. There is no layered rendering and no
/// geometry shader involved.
/// </para>
/// <para>
/// Two details that silently break everything if missed: the vertex stages expect a <b>half-unit
/// quad</b> (they do <c>gl_Position.xy = in_attr0.xy * 2.0</c>), and every one of these shaders
/// divides by <c>support_buffer.render_scale[0]</c>, so <see cref="SupportBufferUbo"/> must be
/// bound or every pass divides by zero.
/// </para>
/// </remarks>
public sealed class SkyPrecomputePass : IDisposable
{
    // ---- RECOVERED FROM A REAL CAPTURE, not chosen ----
    // The whole SizeInfo block was read out of the game's own bound constant buffer (a sky bake
    // draw), found by searching every draw's constant buffers for a vec4 shaped (a, b, 1/a, 1/b).
    // That search was necessary because the block cannot be identified by size: the emulator
    // declares every block as data[4096] and rounds UBO bindings up to 256-byte alignment, so a
    // 144-byte struct is indistinguishable from a 224-byte one. Ground truth, all nine slots:
    //   [0] 256, 64,  1/256, 1/64      [4] 32, 1/32, 1/31, 1/15
    //   [1] 64,  64,  1/64,  1/64      [5] 32, 1/32, 1/31, 1/15
    //   [2] 256, 256, 1/256, 1/256     [6] 8,  1/8,  1/7,  1/3
    //   [3] 64,  64,  1/64,  1/64      [7] 16, 1/16, 1/15, 1/7
    //   [8] 6360, 6420, 0, 0
    //
    // Both values this class previously had to ASSUME - a 256x64 transmittance table and
    // Rg/Rt = 6360/6420 - came back exactly right. They were derived independently from the shader
    // maths beforehand, so the agreement is a real check rather than a coincidence.
    public const int TransmittanceW = 256;
    public const int TransmittanceH = 64;
    public const int IrradianceW = 64;
    public const int IrradianceH = 64;
    public const int BakedInscatterW = 256;
    public const int BakedInscatterH = 256;
    public const int RangeTransmittanceW = 64;
    public const int RangeTransmittanceH = 64;

    // The four inscatter axes. NOT Bruneton's reference numbers (R=32, MU=128, MU_S=32, NU=8) -
    // TotK uses a coarser table - which is exactly why guessing them would have produced a
    // wrong-but-plausible LUT rather than an obviously broken one. Corroborated three ways:
    // SizeInfo itself, sky_copy_inscatter's vertex shader computing data[5].x * data[6].x for the
    // packed width, and the real inscatter texture in the capture being allocated 256 x 32 x 16.
    public const int ResMu = 32;    // slot 4 - the packed face's height
    public const int ResMuS = 32;   // slot 5
    public const int ResNu = 8;     // slot 6 - ResMuS * ResNu = 256, the packed width
    public const int ResR = 16;     // slot 7 - depth; the axis the per-slice layer index walks

    public const int InscatterW = ResMuS * ResNu;   // 256
    public const int InscatterH = ResMu;            // 32
    public const int InscatterD = ResR;             // 16

    /// <summary>Ground (planet) radius. Confirmed against the capture, and consistent with the ROM's own scale heights being in kilometres (<c>RayleighBaseHeight</c> = 24, <c>MieBaseHeight</c> = 2).</summary>
    public const float Rg = 6360f;
    /// <summary>Top-of-atmosphere radius; <c>Rt - Rg</c> = 60 km. Confirmed against the capture.</summary>
    public const float Rt = 6420f;

    /// <summary>
    /// Multiple-scattering orders to solve. Defaults to 6, chosen by measurement rather than by
    /// copying Bruneton's reference 4: sweeping against the game's own captured inscatter LUT gave
    /// mean per-slice correlations of 0.9016 / 0.9392 / 0.9492 / 0.9523 at 2 / 4 / 6 / 8 orders,
    /// with the green channel landing on 1.0000 of the real mean at 6. Past 6 it is converging
    /// (+0.003 for another two full passes over the 3D LUTs), so 6 is where the curve flattens.
    /// Override with <c>WRS_SKY_ORDERS</c> (or the older <c>MARROW_SKY_ORDERS</c>).
    /// </summary>
    public int ScatteringOrders { get; set; } =
        int.TryParse((Environment.GetEnvironmentVariable("WRS_SKY_ORDERS") ?? Environment.GetEnvironmentVariable("MARROW_SKY_ORDERS")), out int o) && o >= 1 ? o : 6;

    // Bindings of our own choosing. The decompiled per-stage NVN indices collide once both stages
    // are linked into one desktop-GL program (the same problem CloudDomePass documents), so every
    // block is rebound explicitly after linking rather than trusting the declared numbers.
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

    // Every program in the chain, with which block each of its fp_c4/vp_c4 slots means. fp_c3 is
    // always SizeInfo. Sampler units are assigned by the fp_t_tcb_<hex> naming, where the hex slot
    // is 8 + 2*<the archive's sampler Location> - verified against five extracted programs; note
    // it is NOT the archive's list order, so binding by position silently swaps textures.
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

    // The copy and bake passes take RenderInfo in c4; every solve pass takes Config there.
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
            BindBlock(prog, "_fp_c3", SizeInfoBinding);
            BindBlock(prog, "_vp_c3", SizeInfoBinding);
            BindBlock(prog, "_fp_c4", c4);
            BindBlock(prog, "_vp_c4", c4);

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

    /// <summary>
    /// Points each program's samplers at fixed texture units. Names encode the constant-buffer
    /// slot: <c>fp_t_tcb_&lt;hex&gt;</c> where hex = 8 + 2*Location, so unit assignment has to
    /// follow the shader's own naming rather than binding order.
    /// </summary>
    void AssignSamplerUnits(uint prog, string name)
    {
        switch (name)
        {
            case "agl_sky_irradiance_step1":            // cTexTransmittance
            case "agl_sky_inscatter_step1":             // cTexTransmittance
            case "agl_sky_irradiance_step3":            // cTexDeltaSR
            case "agl_sky_copy_irradiance_step0":       // cTexDeltaE
            case "agl_sky_bake_inscatter":              // cTexInscatter
                SetSamplerUnit(prog, "fp_t_tcb_8", 0);
                break;

            case "agl_sky_irradiance_step2":            // SR@0, SM@1
            case "agl_sky_copy_inscatter_step1":        // SR@0, SM@1
            case "agl_sky_inscatter_step2":             // T@0, deltaJ@1
                SetSamplerUnit(prog, "fp_t_tcb_8", 0);
                SetSamplerUnit(prog, "fp_t_tcb_A", 1);
                break;

            case "agl_sky_copy_inscatter_step2":        // cTexDeltaSR only
                SetSamplerUnit(prog, "fp_t_tcb_8", 0);
                break;

            case "agl_sky_delta_inscatter_step2":       // T@0, E@1, SR@2, SM@3
                SetSamplerUnit(prog, "fp_t_tcb_8", 0);
                SetSamplerUnit(prog, "fp_t_tcb_A", 1);
                SetSamplerUnit(prog, "fp_t_tcb_C", 2);
                SetSamplerUnit(prog, "fp_t_tcb_E", 3);
                break;

            case "agl_sky_delta_inscatter_step3":       // T@0, E@1, SR@2
                SetSamplerUnit(prog, "fp_t_tcb_8", 0);
                SetSamplerUnit(prog, "fp_t_tcb_A", 1);
                SetSamplerUnit(prog, "fp_t_tcb_C", 2);
                break;
        }
    }

    static unsafe uint CreateLut2D(GL gl, int w, int h)
    {
        uint tex = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, tex);
        // 16F to match the game's own allocation (the captured inscatter LUT is RGBA16F), and
        // because transmittance spans orders of magnitude along a horizon-grazing ray.
        gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba16f, (uint)w, (uint)h, 0,
            PixelFormat.Rgba, PixelType.Float, null);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        // Clamp, never repeat: these are parameterisation tables, and wrapping an edge would fold
        // a grazing ray back onto a zenith ray.
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
        gl.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        gl.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        gl.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        gl.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
        gl.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureWrapR, (int)GLEnum.ClampToEdge);
        return tex;
    }

    bool BindBlock(uint program, string name, uint binding)
    {
        uint idx = _gl.GetUniformBlockIndex(program, name);
        if (idx == 0xFFFFFFFFu)
            return false;
        _gl.UniformBlockBinding(program, idx, binding);
        return true;
    }

    void SetSamplerUnit(uint program, string name, int unit)
    {
        int loc = _gl.GetUniformLocation(program, name);
        if (loc >= 0)
            _gl.Uniform1(loc, unit);
    }

    // ---------------- uniform blocks ----------------

    /// <summary>The 144-byte <c>SizeInfo</c> blob, reproducing the game's own bound contents exactly.</summary>
    internal static byte[] BuildSizeInfo()
    {
        var buf = new byte[144];
        void F(int slot, int comp, float v) => BitConverter.GetBytes(v).CopyTo(buf, slot * 16 + comp * 4);
        void Dims(int slot, int w, int h)
        {
            F(slot, 0, w); F(slot, 1, h);
            F(slot, 2, 1f / w); F(slot, 3, 1f / h);
        }
        // (n, 1/n, 1/(n-1), 1/(n/2 - 1)) - the shape every axis slot has in the capture. The last
        // two are texel-centre remap constants: 1/(n-1) spans texel centres across the whole axis,
        // and 1/(n/2 - 1) does the same for a half-resolution walk.
        void Axis(int slot, int n)
        {
            F(slot, 0, n);
            F(slot, 1, 1f / n);
            F(slot, 2, 1f / (n - 1));
            F(slot, 3, 1f / (n / 2 - 1));
        }

        Dims(0, TransmittanceW, TransmittanceH);
        Dims(1, IrradianceW, IrradianceH);
        Dims(2, BakedInscatterW, BakedInscatterH);
        Dims(3, RangeTransmittanceW, RangeTransmittanceH);
        Axis(4, ResMu);
        Axis(5, ResMuS);
        Axis(6, ResNu);
        Axis(7, ResR);
        F(8, 0, Rg);
        F(8, 1, Rt);
        return buf;
    }

    /// <summary>
    /// The real <c>RenderInfo</c> the game binds to <c>sky_bake_inscatter</c>, as captured. Slots
    /// whose meaning is known are overwritten from live data by <see cref="BuildBakeRenderInfo"/>;
    /// the rest are kept because zeroing them is DEMONSTRABLY wrong, not merely unverified.
    /// </summary>
    /// <remarks>
    /// The bake was originally handed a block containing only a layer index - which was wrong
    /// twice over. It is a 2D pass over the whole 3D table, so it has no layer at all (the layer
    /// convention belongs to <c>copy_inscatter</c>), and more importantly this block carries
    /// <c>betaR</c>/<c>betaM</c> at <c>[0]</c>: those are the weights that combine the Rayleigh and
    /// Mie tables into colour, so with them zero the LUT came out with a perfectly healthy ALPHA
    /// and identically zero RGB - a black sky.
    ///
    /// Same reasoning as <c>CloudUboBaseline</c>: start from real captured bytes, overwrite only
    /// what is understood.
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

        void F(int slot, int comp, float v) => BitConverter.GetBytes(v).CopyTo(buf, slot * 16 + comp * 4);

        var br = postfx.RayleighScatteringCoeff;
        F(0, 0, br.X); F(0, 1, br.Y); F(0, 2, br.Z); F(0, 3, postfx.MieScatteringCoeff);

        // Y-up, like every other agl sky/cloud input.
        Vector3 sun = new(sunWorldZUp.X, sunWorldZUp.Z, sunWorldZUp.Y);
        if (sun.LengthSquared() > 1e-12f) sun = Vector3.Normalize(sun);
        F(2, 0, sun.X); F(2, 1, sun.Y); F(2, 2, sun.Z);

        var g = postfx.GroundColor;
        F(6, 0, g.X); F(6, 1, g.Y); F(6, 2, g.Z); F(6, 3, 1f);
        return buf;
    }

    /// <summary>
    /// The 48-byte <c>Config</c> blob: the ROM's own atmosphere parameters, plus Bruneton's
    /// <c>dhdH</c> for one altitude slice.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>[2] = dhdH = (dmin, dmax, dminp, dmaxp)</c> is inference, but with an exact identity
    /// behind it rather than a resemblance. The shaders clamp against this slot in two distinct
    /// PAIRS - <c>max(t, [2].x)</c> with <c>min(t, [2].y * 0.999)</c>, and <c>max(t, [2].z)</c>
    /// with <c>min(t, [2].w * 0.999)</c> - which is exactly how Bruneton clamps the sky-branch and
    /// ground-branch ray lengths against <c>(dmin, dmax)</c> and <c>(dminp, dmaxp)</c>. The clincher
    /// is that <c>inscatter_step1</c> recovers the sphere radius as <c>[2].z + Rg</c>, and in
    /// Bruneton <c>dminp</c> is defined as exactly <c>r - Rg</c>. A slot that yields the radius
    /// when you add the ground radius to it IS <c>dminp</c>.
    /// </para>
    /// <para>
    /// <b><c>[1].x</c> is read but NOT identified, and is deliberately left zero.</b> It is read by
    /// exactly four passes - <c>delta_inscatter_step2/3</c>, <c>irradiance_step2</c> and
    /// <c>bake_inscatter</c> - i.e. only the multiple-scattering source terms, and by none of the
    /// single-scattering or transmittance passes. That narrows it to a constant those terms need
    /// and the others do not (Bruneton's candidates there are the Mie phase asymmetry and the
    /// average ground reflectance). The Mie-asymmetry reading was TESTED against the real captured
    /// inscatter LUT and rejected: feeding <c>MieSymmetricalPropRendering</c> (0.85) moved the blue
    /// channel from 0.702 to 0.704 of the real mean and slightly WORSENED correlation
    /// (0.9392 -> 0.9383), so it is not that. Zero is kept because it is at least a known,
    /// documented omission rather than a wrong value dressed up as a real one; this slot is the
    /// leading suspect for the residual ~20% blue deficit noted in the class remarks.
    /// </para>
    /// <para>
    /// <c>[1].w</c> IS genuinely unread by every program in the chain, so it stays zero.
    /// </para>
    /// </remarks>
    internal static byte[] BuildConfig(SkyPostFx postfx, int layer,
        float rayleighAmplifier = 1f, float mieAmplifier = 1f)
    {
        var buf = new byte[48];
        void F(int slot, int comp, float v) => BitConverter.GetBytes(v).CopyTo(buf, slot * 16 + comp * 4);

        // The palette's own amplifiers scale the AAMP's coefficients. This is what makes the sky
        // change COLOUR per palette rather than only brightness: the LUT is solved from betaR/betaM,
        // so a palette that scales Rayleigh against Mie differently produces a different-coloured
        // atmosphere, which is exactly what a red moon or a storm palette is.
        var br = postfx.RayleighScatteringCoeff * rayleighAmplifier;
        F(0, 0, br.X);
        F(0, 1, br.Y);
        F(0, 2, br.Z);
        F(0, 3, postfx.MieScatteringCoeff * mieAmplifier);
        F(1, 1, postfx.RayleighBaseHeight);
        F(1, 2, postfx.MieBaseHeight);

        var (dmin, dmax, dminp, dmaxp) = SliceGeometry(layer);
        F(2, 0, dmin);
        F(2, 1, dmax);
        F(2, 2, dminp);
        F(2, 3, dmaxp);
        return buf;
    }

    /// <summary>
    /// Bruneton's per-layer radius and the four ray-length bounds derived from it.
    /// </summary>
    /// <remarks>
    /// The epsilon nudges at the first and last layer are load-bearing, not cosmetic: at exactly
    /// <c>r == Rg</c> the horizon term <c>sqrt(r*r - Rg*Rg)</c> is 0 and at exactly <c>r == Rt</c>
    /// the sky-branch length collapses, either of which puts a division on a knife edge and
    /// produces NaN across a whole slice.
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

    /// <summary>The 128-byte <c>RenderInfo</c> blob. <c>[3].w</c> is the normalised layer.</summary>
    /// <remarks>
    /// <c>sky_copy_inscatter</c>'s vertex shader computes <c>data[7].x * L - L + 0.5</c>, i.e.
    /// <c>L * (ResR - 1) + 0.5</c>. For that to land on the texel centre of slice <c>i</c>, L must
    /// be <c>i / (ResR - 1)</c>.
    /// </remarks>
    internal static byte[] BuildRenderInfo(int layer)
    {
        var buf = new byte[128];
        BitConverter.GetBytes(layer / (float)(ResR - 1)).CopyTo(buf, 3 * 16 + 3 * 4);
        return buf;
    }

    // ---------------- pass plumbing ----------------

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

    // ---------------- the chain ----------------

    /// <summary>Runs the whole precompute once. Cheap to guard - the caller does that.</summary>
    /// <param name="sunWorldZUp">WildRenderingSharp's Z-up sun direction, converted internally for the bake pass.</param>
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

        // The atmosphere inputs, logged because they are the ONLY thing distinguishing this run
        // from the game's own: everything else (dimensions, radii, pass order, the shaders) is now
        // pinned. A spectral mismatch against a captured LUT shows up here first.
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

        // E starts at zero: Bruneton accumulates irradiance from order 2 onward, and the direct
        // term above belongs to deltaE only.
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

            // 5a. deltaJ - the first iteration still has a separate Mie delta to fold in, which is
            // exactly what the step2/step3 split of these programs is for.
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

            // 5d/5e. accumulate into E and S. Additive, which is the whole point of these two
            // "copy" programs - they are the += of the iteration.
            _gl.Enable(EnableCap.Blend);
            _gl.BlendFunc(BlendingFactor.One, BlendingFactor.One);
            // Explicit for the same reason as the cloud composite - and it matters more here,
            // because this accumulation is BAKED into the inscatter LUT: a leaked subtract equation
            // would not just tint one frame, it would persist in the LUT until the next re-bake.
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

        // 6. collapse the 4D table into the 2D LUT the per-frame sky shader samples. This pass
        // needs the REAL RenderInfo, not the layer-only one the copy passes take - it carries the
        // betaR/betaM that turn the Rayleigh and Mie tables into colour.
        Bind(0, TextureTarget.Texture3D, _inscatter);
        resources.Ubo("sky_bake_renderinfo", BuildBakeRenderInfo(postfx, _sunWorld), RenderInfoBinding);
        Target2D(_bakedInscatter, BakedInscatterW, BakedInscatterH);
        DrawQuad("agl_sky_bake_inscatter");

        ApplySpectralCalibration();

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        _gl.ActiveTexture(TextureUnit.Texture0);
    }

    // ---------------- spectral calibration ----------------

    /// <summary>
    /// Per-channel gain applied to the finished LUT. Neutral by default.
    /// </summary>
    /// <remarks>
    /// This used to be <c>(1/1.166, 1, 1/0.770)</c> - the reciprocals of a measured mismatch against
    /// the game's own captured inscatter table. That measurement was real, but it was taken against
    /// exactly ONE palette (a noon blue sky) and then applied to every palette, where it is actively
    /// harmful: it cuts red 14% and boosts blue 30%, which drags a saturated red from 0.79
    /// saturation down to 0.68 and visibly toward orange. It was compensating for an unexplained
    /// residual (see <see cref="BuildConfig"/> on <c>Config[1].x</c>), and compensating for an
    /// unknown with a constant measured on one sample is exactly how a fudge becomes a bug.
    ///
    /// The LUT-peak normalisation and the per-palette tint now do the jobs this was standing in for,
    /// so it is neutral. Kept as a hook because the underlying residual is still unexplained.
    /// </remarks>
    public Vector3 SpectralCalibration { get; set; } = Vector3.One;

    /// <summary>
    /// Per-palette colour folded into the baked LUT, 0 = none.
    /// </summary>
    /// <remarks>
    /// Applied as a per-channel MULTIPLY on the finished LUT rather than through the sky shader's
    /// blend bias, because that bias cannot do the job: the shader computes
    /// <c>clamp(lut.a + RenderInfo[6].w)</c>, so wherever the LUT's alpha is already 1 - which is
    /// most of a bright sky - the weight saturates and the palette colour is ignored entirely. A
    /// multiply always has an effect, and it SATURATES rather than washing toward the tint, which
    /// is what a blood moon actually looks like.
    /// </remarks>
    public Vector3 PaletteTint { get; set; } = Vector3.One;

    /// <summary>
    /// Peak value the baked LUT is normalised to, so palettes with wildly different scattering
    /// amplifiers land in the same range instead of clipping.
    /// </summary>
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

    /// <summary>
    /// Multiplies the baked LUT in place by <see cref="SpectralCalibration"/>, via a scratch copy
    /// (a texture cannot be its own render target and sampler in one pass).
    /// </summary>
    void ApplySpectralCalibration()
    {
        // Normalise the LUT's PEAK before tinting. A palette's scattering amplifiers change the
        // table's absolute magnitude enormously - BloodyMoon_DarknessDragon's mie x256 takes the
        // peak from ~60 to ~427 - and since this is painted into a buffer that then gets a fixed
        // Exposure, a 7x brighter table simply clips. Clipping is what destroys the colour: the red
        // channel pins at 1 while green and blue keep climbing, so a deep red reads as pink and
        // then white, which is exactly the failure this kept coming back as.
        //
        // Normalising decouples the atmosphere's SHAPE and HUE (which is what the solve gives us)
        // from its absolute magnitude (which our exposure pipeline is only a stand-in for anyway),
        // and leaves brightness to the palette's BgDifIntensity and the Atmosphere Intensity
        // slider - controls that exist for exactly that job.
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

    // ---------------- verification ----------------

    /// <summary>
    /// The real <c>SizeInfo</c> bytes as read from the game, for checking <see cref="BuildSizeInfo"/>
    /// reproduces them.
    /// </summary>
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
    /// Reports each LUT's range and finiteness, and checks the properties any correct table must
    /// have. Returns true if nothing is obviously broken.
    /// </summary>
    /// <remarks>
    /// A precompute LUT has no on-screen appearance to judge, so "it rendered without a GL error"
    /// proves almost nothing here - the project's working agreement is to prove a step with data.
    /// The strongest signal is NaN/negative counts: every quantity in this chain is a radiance or a
    /// transmittance and so must be finite and non-negative, and the usual failure modes (a wrong
    /// dhdH putting a division on a knife edge, a sampler bound to the wrong texture, a slice
    /// rendered with the wrong Config) all surface as NaN or negatives rather than as plausible
    /// numbers.
    /// </remarks>
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
        // RGB and alpha separately: the sky shader multiplies RGB by an intensity and uses ALPHA
        // as the blend weight, so an all-zero RGB with a healthy alpha reads as a perfectly normal
        // [0,1] range while rendering pure black.
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

        // Optional escape hatch for the one check that beats every heuristic above: diffing the
        // computed inscatter table against the real one lifted out of a GPU capture. Off unless
        // asked for, since it writes an 8 MB file.
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
