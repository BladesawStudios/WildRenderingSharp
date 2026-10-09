using System.Numerics;
using Silk.NET.OpenGL;
using WildRenderingSharp.Gpu;
using WildRenderingSharp.Pipeline.Resources;
using WildRenderingSharp.Profiles.Totk.Atmosphere.Palettes;
using WildRenderingSharp.Shaders;

namespace WildRenderingSharp.Profiles.Totk.Sky.Precompute;

/// <summary>
/// Runs the <c>agl::pfx::Sky</c> precompute chain (Bruneton's multiple-scattering solve), whose end product is the
/// <c>cTexBakedInscatter</c> table that <c>agl_sky_postfx_sky</c> only samples.
/// </summary>
internal sealed class SkyPrecomputePass : IDisposable
{
    // SizeInfo, read from the game's constant buffer during a bake draw: the 2D tables, then the four inscatter axes and the radii.
    public const int TransmittanceW = 256;
    public const int TransmittanceH = 64;
    public const int IrradianceW = 64;
    public const int IrradianceH = 64;
    public const int BakedInscatterW = 256;
    public const int BakedInscatterH = 256;
    public const int RangeTransmittanceW = 64;
    public const int RangeTransmittanceH = 64;

    // TotK's inscatter table is coarser than Bruneton's reference; SizeInfo, the copy pass's packed width and the captured texture all agree.
    public const int ResMu = 32;
    public const int ResMuS = 32;
    public const int ResNu = 8;
    public const int ResR = 16;

    public const int InscatterW = ResMuS * ResNu;
    public const int InscatterH = ResMu;
    public const int InscatterD = ResR;

    public const float Rg = 6360f;
    public const float Rt = 6420f;

    static readonly string CalibVert = GlslFiles.Load("Totk/Sky/SkyPrecompute/Calib.vert");
    static readonly string CalibFrag = GlslFiles.Load("Totk/Sky/SkyPrecompute/Calib.frag");

    readonly GL _gl;
    readonly SkyTables? _tables;
    readonly SkyCanvas? _canvas;
    Vector3 _sunWorld = Vector3.UnitY;
    SkyLook _look = SkyLook.Noon;
    uint _calibProgram;

    public SkyPrecomputePass(GL gl, ShaderProgramCache programs)
    {
        _gl = gl;
        if (SkyChainPrograms.Load(gl, programs) is not { } chain)
            return;

        _canvas = new SkyCanvas(gl, chain);
        _tables = new SkyTables(gl);
        Console.WriteLine($"[SkyPrecomputePass] linked all real agl sky programs; inscatter LUT {InscatterW}x{InscatterH}x{InscatterD}, transmittance {TransmittanceW}x{TransmittanceH}.");
    }

    public bool Available => _tables is not null;

    public uint BakedInscatter => _tables?.BakedInscatter ?? 0;

    public int ScatteringOrders { get; set; } =
        int.TryParse(Environment.GetEnvironmentVariable("WRS_SKY_ORDERS") ?? Environment.GetEnvironmentVariable("MARROW_SKY_ORDERS"), out int orders) && orders >= 1 ? orders : 6;

    public Vector3 SpectralCalibration { get; set; } = Vector3.One;

    public Vector3 PaletteTint { get; set; } = Vector3.One;

    // The table's v axis is the view direction's y component mapped to 0..1, so 0.5 is the horizon; 0.58 is about 9 degrees above it.
    public float HorizonClampY { get; set; } = 0.58f;

    public void Run(GLResourceCache resources, SkyPostFx postfx, Vector3 sunWorld = default, Vector3? paletteTint = null, SkyLook? look = null)
    {
        _look = look ?? SkyLook.Noon;
        PaletteTint = paletteTint ?? Vector3.One;
        _sunWorld = sunWorld == default ? Vector3.UnitY : sunWorld;
        if (_tables is null || _canvas is null)
            return;

        resources.Bind(SupportBuffer.Block);
        resources.Bind(SkyPrecomputeBlocks.BuildSizeInfo());
        LogAtmosphere(postfx);

        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.CullFace);
        _gl.Disable(EnableCap.Blend);

        SolveTransmittanceAndIrradiance(resources, postfx, _tables, _canvas);
        SolveSingleScattering(resources, postfx, _tables, _canvas);
        for (int order = 2; order <= ScatteringOrders; order++)
            SolveOrder(resources, postfx, _tables, _canvas, order);
        Bake(resources, postfx, _tables, _canvas);

        ApplySpectralCalibration(_tables, _canvas);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        _gl.ActiveTexture(TextureUnit.Texture0);
    }

    public bool Verify() => _tables is not null && SkyPrecomputeVerification.Run(_gl, _tables, ScatteringOrders);

    public void Dispose()
    {
        _tables?.Dispose();
        _canvas?.Dispose();
        if (_calibProgram != 0)
            _gl.DeleteProgram(_calibProgram);
    }

    // The atmosphere inputs are all that distinguishes this run from the game's; a spectral mismatch shows up here first.
    static void LogAtmosphere(SkyPostFx postfx)
    {
        var rayleigh = postfx.RayleighScatteringCoeff;
        Console.WriteLine($"[SkyPrecomputePass] atmosphere: betaR=({rayleigh.X:G6}, {rayleigh.Y:G6}, {rayleigh.Z:G6}) " +
            $"betaM={postfx.MieScatteringCoeff:G6} HR={postfx.RayleighBaseHeight:G6} HM={postfx.MieBaseHeight:G6}");
    }

    void BindConfig(GLResourceCache resources, SkyPostFx postfx, int layer) =>
        resources.Bind(SkyPrecomputeBlocks.BuildConfig(postfx, layer, 1f, 1f, _look.MieAsymmetry));

    // Steps 1 and 2: the transmittance table, then the ground irradiance from direct sunlight.
    void SolveTransmittanceAndIrradiance(GLResourceCache resources, SkyPostFx postfx, SkyTables t, SkyCanvas canvas)
    {
        BindConfig(resources, postfx, 0);
        canvas.Target2D(t.Transmittance, TransmittanceW, TransmittanceH);
        canvas.Draw("agl_sky_transmittance_step0");

        canvas.BindInput(0, TextureTarget.Texture2D, t.Transmittance);
        canvas.Target2D(t.DeltaE, IrradianceW, IrradianceH);
        canvas.Draw("agl_sky_irradiance_step1");

        // Irradiance accumulates from order 2 onward, so it starts at zero; the direct term belongs to deltaE only.
        canvas.Target2D(t.Irradiance, IrradianceW, IrradianceH);
        canvas.Clear();
    }

    // Steps 3 and 4: single scattering, Rayleigh and Mie written together by the one multi-target pass, then their sum.
    void SolveSingleScattering(GLResourceCache resources, SkyPostFx postfx, SkyTables t, SkyCanvas canvas)
    {
        canvas.BindInput(0, TextureTarget.Texture2D, t.Transmittance);
        for (int layer = 0; layer < ResR; layer++)
        {
            BindConfig(resources, postfx, layer);
            canvas.TargetLayerPair(t.DeltaSR, t.DeltaSM, layer);
            canvas.Draw("agl_sky_inscatter_step1");
        }

        canvas.BindInput(0, TextureTarget.Texture3D, t.DeltaSR);
        canvas.BindInput(1, TextureTarget.Texture3D, t.DeltaSM);
        DrawLayers(resources, canvas, t.Inscatter, "agl_sky_copy_inscatter_step1");
    }

    // Step 5: one further order of scattering. The first folds in the separate Mie delta, which is why its programs differ.
    void SolveOrder(GLResourceCache resources, SkyPostFx postfx, SkyTables t, SkyCanvas canvas, int order)
    {
        bool first = order == 2;

        canvas.BindInput(0, TextureTarget.Texture2D, t.Transmittance);
        canvas.BindInput(1, TextureTarget.Texture2D, t.DeltaE);
        canvas.BindInput(2, TextureTarget.Texture3D, t.DeltaSR);
        if (first)
            canvas.BindInput(3, TextureTarget.Texture3D, t.DeltaSM);
        for (int layer = 0; layer < ResR; layer++)
        {
            BindConfig(resources, postfx, layer);
            canvas.TargetLayer(t.DeltaJ, layer);
            canvas.Draw(first ? "agl_sky_delta_inscatter_step2" : "agl_sky_delta_inscatter_step3");
        }

        canvas.BindInput(0, TextureTarget.Texture3D, t.DeltaSR);
        if (first)
            canvas.BindInput(1, TextureTarget.Texture3D, t.DeltaSM);
        BindConfig(resources, postfx, 0);
        canvas.Target2D(t.DeltaE, IrradianceW, IrradianceH);
        canvas.Draw(first ? "agl_sky_irradiance_step2" : "agl_sky_irradiance_step3");

        canvas.BindInput(0, TextureTarget.Texture2D, t.Transmittance);
        canvas.BindInput(1, TextureTarget.Texture3D, t.DeltaJ);
        for (int layer = 0; layer < ResR; layer++)
        {
            BindConfig(resources, postfx, layer);
            canvas.TargetLayer(t.DeltaSR, layer);
            canvas.Draw("agl_sky_inscatter_step2");
        }

        AccumulateOrder(resources, t, canvas);
    }

    // The copy programs are the += of the iteration, so they blend additively. The equation is explicit because the accumulation is
    // baked into the table, and a leaked subtract would persist until the next bake.
    void AccumulateOrder(GLResourceCache resources, SkyTables t, SkyCanvas canvas)
    {
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.One, BlendingFactor.One);
        _gl.BlendEquation(BlendEquationModeEXT.FuncAdd);

        canvas.BindInput(0, TextureTarget.Texture2D, t.DeltaE);
        canvas.Target2D(t.Irradiance, IrradianceW, IrradianceH);
        canvas.Draw("agl_sky_copy_irradiance_step0");

        canvas.BindInput(0, TextureTarget.Texture3D, t.DeltaSR);
        DrawLayers(resources, canvas, t.Inscatter, "agl_sky_copy_inscatter_step2");

        _gl.Disable(EnableCap.Blend);
    }

    void DrawLayers(GLResourceCache resources, SkyCanvas canvas, uint target, string program)
    {
        for (int layer = 0; layer < ResR; layer++)
        {
            resources.Bind(SkyPrecomputeBlocks.BuildLayerRenderInfo(layer));
            canvas.TargetLayer(target, layer);
            canvas.Draw(program);
        }
    }

    // Step 6: the 4D table collapses into the 2D table the per-frame sky shader samples. This pass needs the full RenderInfo, which carries betaR and betaM.
    void Bake(GLResourceCache resources, SkyPostFx postfx, SkyTables t, SkyCanvas canvas)
    {
        canvas.BindInput(0, TextureTarget.Texture3D, t.Inscatter);
        resources.Bind(SkyPrecomputeBlocks.BuildBakeRenderInfo(postfx, _sunWorld, _look));
        canvas.Target2D(t.BakedInscatter, BakedInscatterW, BakedInscatterH);
        canvas.Draw("agl_sky_bake_inscatter");
    }

    // Multiplies the baked table in place by the calibration, through a scratch copy, since a texture cannot be its own target and sampler.
    void ApplySpectralCalibration(SkyTables t, SkyCanvas canvas)
    {
        if (_calibProgram == 0)
            _calibProgram = GLProgramBuilder.Build(_gl, CalibVert, CalibFrag, "sky_spectral_calibration");

        uint scratch = SkyTables.CreateTable2D(_gl, BakedInscatterW, BakedInscatterH);
        canvas.Target2D(scratch, BakedInscatterW, BakedInscatterH);
        _gl.UseProgram(_calibProgram);
        _gl.BindTextureUniform(_calibProgram, "tSrc", 0, t.BakedInscatter);
        _gl.SetVec3(_calibProgram, "uGain", SpectralCalibration * PaletteTint);
        _gl.SetFloat(_calibProgram, "uHorizonY", HorizonClampY);
        canvas.Draw();

        _gl.DeleteTexture(t.BakedInscatter);
        t.BakedInscatter = scratch;
    }
}
