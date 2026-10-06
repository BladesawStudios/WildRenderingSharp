using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.OpenGL;
using WildRenderingSharp.Assets;
using WildRenderingSharp.Rendering;
using WildRenderingSharp.Shaders.Profiles.Totk.Ubos;

namespace WildRenderingSharp.Pipeline;

/// <summary>One placed actor's per-frame render input - its own placement transform and (if a skeletal clip is applied) its own animated bone pose. See <see cref="DeferredPipeline"/>'s remarks for why every actor needs its own bone-palette/ShpMtx buffer rebound per-actor rather than one shared global one.</summary>
public sealed record ActorRenderInput(LoadedModel Model, Vector4[] ModelMatrixRows, Matrix4x4[]? BoneWorldMatrices = null);

/// <summary>
/// Everything a caller needs to render one frame - the current camera, lighting/palette state,
/// and every placed actor's own transform/pose.
/// </summary>
/// <param name="Highlight">Which actor/shape to overlay with a flat, translucent highlight this frame (e.g. a hovered row in the Material Inspector), or null for none - see <see cref="HighlightOverlayPass"/>.</param>
/// <param name="Instances">Batches of placements drawn instanced alongside <paramref name="Actors"/> - a map's worth of static objects (see <see cref="InstanceBatch"/>). Each draws its own <see cref="InstanceBatch.Visible"/> runs.</param>
/// <param name="Terrain">A host whose terrain is shaded with the game's terrain programs - see <see cref="TerrainShading"/>.</param>
/// <param name="ShadowCascades">
/// Nested shadow regions, finest first, each drawn into its own 2048x2048 layer (at most
/// <see cref="RenderTargets.MaxCascades"/>): near shadows sharp, far ones coarse but present, as
/// the game's own cascades are. Each cascade is redrawn only when its region, the sun or its casters
/// change - a host snapping each region to a grid of its own size makes the far ones nearly free.
/// Takes the place of <paramref name="ShadowFocus"/> when given.
/// </param>
/// <param name="ShadowFocus">The region the shadow map covers, instead of every actor's bounds - for a scene far bigger than one shadow map can resolve, centred on what the camera looks at.</param>
public sealed record FrameRequest(Camera Camera, LightingContext Lighting, EnvPalette Palette, IReadOnlyList<ActorRenderInput> Actors, float AoRadius, float ShadowBias, (int ActorIndex, int ShapeIndex)? Highlight = null, SkyPostFx? SkyPostFx = null, CloudPostFx? CloudPostFx = null, SkyBinLut? SkyBin = null, ColorCorrectionPostFx? ColorCorrection = null,
    IReadOnlyList<InstanceBatch>? Instances = null, ShadowFocus? ShadowFocus = null,
    IReadOnlyList<ShadowFocus>? ShadowCascades = null,
    ITerrainHost? Terrain = null);

/// <summary>A sphere the shadow map is fitted to - see <see cref="FrameRequest.ShadowFocus"/>.</summary>
public readonly record struct ShadowFocus(Vector3 Center, float Radius);

/// <summary>The handful of intermediate targets a viewer might want to display directly, alongside the final tonemapped result - matches <c>viewer.Viewer</c>'s view-mode switch (final/HDR/albedo/normal/shadow/AO), plus the pass-ID mask as a diagnostic for multi-pass models.</summary>
public readonly record struct FrameResult(GpuTexture Ldr, GpuTexture Final, GpuTexture Albedo, GpuTexture Normal, GpuTexture PreShadow, GpuTexture PreMisc, GpuTexture PassId);

/// <summary>
/// The shadow-map reuse state <see cref="DeferredPipeline.RenderFrame"/> checks every frame (see
/// that method's own remarks) - normally the pipeline's own private cache, but callers rendering a
/// SECOND independent view through a DIFFERENT <see cref="RenderTargets"/> (e.g. a picture-in-
/// picture camera preview) need their OWN instance: the cache decides "reuse last frame's shadow
/// MAP TEXTURE untouched", which is only valid per-RenderTargets (each has its own texture) - two
/// views sharing one cache object would have each one's render alternately invalidate the other's,
/// forcing a full shadow redraw on literally every frame for both instead of caching correctly for
/// either.
/// </summary>
public sealed class ShadowCache
{
    internal Vector3? SunWorld;
    internal Vector4[][]? ModelRowsPerActor;
    /// <summary>A copy of each actor's posed bones the map was drawn with (null entries: bind pose).</summary>
    internal Matrix4x4[]?[]? BonesPerActor;
    internal ShadowFocus? Focus;
    internal long InstanceSignature;
    internal Vector3 RotatedLo, RotatedHi;
    internal ShadowPass.LightMatrices LightMatrices;
    internal readonly long[] CascadeSignature = new long[RenderTargets.MaxCascades];
    internal readonly ShadowPass.LightMatrices[] CascadeLight = new ShadowPass.LightMatrices[RenderTargets.MaxCascades];
    internal readonly float[] CascadeRadius = new float[RenderTargets.MaxCascades];
}

/// <summary>
/// Orchestrates the whole deferred chain - G-buffer, shadow map, linear depth, screen-space
/// shadow/AO, the ID-masked multi-pass deferred resolve, the forward pass for blended materials,
/// bloom, and the real <c>agl_hdr_compose</c> tonemap - owning every persistent GL object so nothing
/// is reallocated mid-frame. Mirrors <c>viewer.Viewer.render_scene</c>'s sequencing exactly; each
/// stage's actual GL work lives in its own pass class (see <c>WildRenderingSharp.Pipeline.*Pass</c>).
///
/// Renders MULTIPLE simultaneously-placed actors through this ONE pipeline instance - shared
/// G-buffer, shared shadow map, shared everything, "just like how the game does it." The real
/// compiled game shaders read bone transforms from two shared, frame-global GPU binding points
/// with NO per-draw instance addressing at all: <c>_Mtx</c> (binding 2, skinned shapes' bone
/// palette) and <c>ShpMtx</c> (binding 4, skin-count-0 "rigid" shapes' single baked transform).
/// A real engine handles this by REBINDING a different object's own buffer immediately before
/// that object's own draw calls - not by trying to combine every on-screen object into one
/// shared buffer (which would also be impossible here regardless: <c>_Mtx</c>'s size, <c>vec4
/// data[4096]</c>, is baked into the real decompiled shader's own GLSL declaration). So every
/// shape-drawing pass below receives an <see cref="ActorDrawGroup"/> list instead of a flat shape
/// list, and rebinds each group's own bones/ShpMtx buffers before drawing that group's shapes -
/// see each pass class's own remarks for exactly how.
/// </summary>
public sealed class DeferredPipeline : IDisposable
{
    readonly GL _gl;
    readonly string _dataDirectory;
    readonly string _decompiledDirectory;
    readonly string _deferredMaterialsDirectory;

    public GLResourceCache Resources { get; }
    public RenderTargets Targets { get; }
    public ShaderProgramCache Programs { get; }

    readonly GBufferPass _gbuffer;
    readonly ShadowPass _shadow;
    readonly LinearDepthPass _linearDepth;
    readonly ScreenSpaceShadowAndAoPass _shadowAo;
    readonly LightPrePass _lightPrePass;
    readonly PassIdMaskPass _passIdMask;
    readonly BackgroundPass _background;
    readonly CloudDomePass _cloudDome;
    readonly SkyPrecomputePass _skyPrecompute;
    readonly SkyPostFxPass _skyPostFx;
    readonly SkyBodyPass _skyBody;
    readonly LensFlarePass _lensFlare;
    readonly ColorCorrectionPass _colorCorrection;

    bool _measureExposureRequested;

    /// <summary>Result of the last <see cref="RequestExposureMeasurement"/>, or null if none/nothing measurable.</summary>
    public ExposureMeter.Result? LastExposureMeasurement { get; private set; }

    /// <summary>
    /// Measure what exposure this scene actually needs on the next frame - see
    /// <see cref="ExposureMeter"/> for why WildRenderingSharp's default exposure (2.5) is a stand-in worth re-deriving.
    /// </summary>
    public void RequestExposureMeasurement() => _measureExposureRequested = true;

    /// <summary>
    /// Value the sky's brightest texel is aimed at BEFORE the tonemap, for a neutral palette.
    /// </summary>
    /// <remarks>
    /// Above 1 on purpose - the frame still has highlight compression and the real
    /// <c>agl_hdr_compose</c> ahead of it, and those are what turn HDR into display range. TotK is
    /// an HDR renderer and relies on that headroom; aiming at 1.0 or below produces a sky that is
    /// technically unclipped and visibly flat.
    /// </remarks>
    const float SkyHdrTargetPeak = 1.8f;
    // The Bruneton chain is view-independent - it depends only on the atmosphere's own authored
    // parameters - so it runs ONCE and its LUTs are reused every frame, which is the entire reason
    // the real per-frame sky shader can be a single texture lookup. Deferred to the first frame
    // rather than the constructor so it runs with real SkyPostFx values off the ROM instead of
    // defaults, and so a failure surfaces in the frame log next to everything else.
    bool _skyPrecomputeDone;
    Vector3 _skyPrecomputeSun = new(0f, 0f, 1f);
    string _skyPrecomputeKey = "\0never";
    readonly DeferredResolvePass _resolve;

    /// <summary>Each pass's GPU time over a recent frame - see <see cref="GpuPassTimer"/>.</summary>
    public GpuPassTimer Timer { get; }

    /// <summary>What the last frame's instanced draws submitted: the G-buffer (prepass and main together), and the shadow cascades - zero when they were reused.</summary>
    public (long Triangles, long Instances) GBufferCounts { get; private set; }

    /// <inheritdoc cref="GBufferCounts"/>
    public (long Triangles, long Instances) ShadowCounts { get; private set; }

    /// <summary>The game's terrain programs, for a host that hands its terrain over (<see cref="FrameRequest.Terrain"/>).</summary>
    public TerrainShading Terrain { get; }

    /// <summary>The pass that lights geometry no actor stamped - the terrain's, <c>o_material_behave</c> 0.</summary>
    const string DefaultPass = "chara_nonmetal";
    const string WaterPass = "field_water";

    /// <summary>Passes a host's terrain needs that no actor may stamp - kept through <see cref="SetScene"/>.</summary>
    readonly List<string> _hostPasses = [];
    bool _terrainDrawn;
    readonly SceneColorShapePass _sceneColorShapes;
    readonly KnownMaterialFixes _knownFixes;
    readonly ForwardPass _forward;
    readonly GridPass _grid;
    readonly HighlightOverlayPass _highlight;
    readonly BloomPass _bloom;
    readonly TonemapPass _tonemap;
    readonly uint _hdrComposeProgram;
    readonly uint _zeroStorageBuffer;

    IReadOnlyList<LoadedModel> _models = [];
    List<LoadedShape> _opaqueShapes = [];
    List<string> _passNames = [];
    bool _cachedNeedsKnownMaterialFixes;
    List<ResolvedDeferredPass> _resolvedPasses = [];

    // The shadow MAP (a light-space depth render of every placed actor) depends only on the sun
    // direction and every actor's own rotation - never the camera - so an orbit/pan drag that
    // moves only the camera can reuse last frame's shadow map untouched instead of redrawing a
    // 2048x2048 depth pass every single mouse-delta. See ShadowCache's own remarks for why this is
    // a distinct object rather than plain fields - RenderFrame's default (main-view) cache.
    readonly ShadowCache _mainShadowCache = new();

    public DeferredPipeline(GL gl, string dataDirectory, string decompiledDirectory, int width, int height,
        string? deferredMaterialsDirectory = null, string? systemTexturesDirectory = null)
    {
        _gl = gl;
        _dataDirectory = dataDirectory;
        _decompiledDirectory = decompiledDirectory;
        // Shared across every model (see DeferredResolvePass.ResolveDeferredPasses's remarks) -
        // defaults to a sibling of decompiledDirectory for a caller that doesn't pass one
        // explicitly, matching how every other the host viewer caller lays out the cache root today.
        _deferredMaterialsDirectory = deferredMaterialsDirectory
            ?? Path.Combine(Path.GetDirectoryName(decompiledDirectory) ?? decompiledDirectory, "_deferred_materials");
        string systemTexturesDir = systemTexturesDirectory
            ?? Path.Combine(Path.GetDirectoryName(decompiledDirectory) ?? decompiledDirectory, "_system_textures");

        // Before any pass builds its programs, so they come from the binary cache too.
        GLProgramBuilder.BinaryCacheDirectory ??=
            Path.Combine(Path.GetDirectoryName(decompiledDirectory) ?? decompiledDirectory, "_glprograms");

        Resources = new GLResourceCache(gl);
        Targets = new RenderTargets(gl, width, height);
        Programs = new ShaderProgramCache(gl, decompiledDirectory);

        _gbuffer = new GBufferPass(gl);
        _shadow = new ShadowPass(gl);
        _linearDepth = new LinearDepthPass(gl);
        _shadowAo = new ScreenSpaceShadowAndAoPass(gl);
        _lightPrePass = new LightPrePass(gl);
        _passIdMask = new PassIdMaskPass(gl);
        _background = new BackgroundPass(gl);
        _cloudDome = new CloudDomePass(gl, Programs, systemTexturesDir);
        _skyPrecompute = new SkyPrecomputePass(gl, Programs);
        _skyPostFx = new SkyPostFxPass(gl, Programs);
        _skyBody = new SkyBodyPass(gl, systemTexturesDir);
        _lensFlare = new LensFlarePass(gl, Programs);
        _colorCorrection = new ColorCorrectionPass(gl);
        _resolve = new DeferredResolvePass(gl);
        Timer = new GpuPassTimer(gl);
        Terrain = new TerrainShading(gl, decompiledDirectory);
        InstancedShaderPatch.BaseInstance = gl.IsExtensionPresent("GL_ARB_shader_draw_parameters");
        _sceneColorShapes = new SceneColorShapePass(gl);
        _knownFixes = new KnownMaterialFixes(gl);
        _forward = new ForwardPass(gl, systemTexturesDir);
        _grid = new GridPass(gl);
        _highlight = new HighlightOverlayPass(gl);
        _bloom = new BloomPass(gl);
        _tonemap = new TonemapPass(gl);
        _hdrComposeProgram = Programs.Load("agl_hdr_compose");

        // A permanently-zeroed shader storage buffer at binding 0 - some decompiled shaders
        // declare one and never meaningfully use it; this just satisfies the binding.
        _zeroStorageBuffer = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, _zeroStorageBuffer);
        gl.BufferData(BufferTargetARB.ShaderStorageBuffer, new byte[65536], BufferUsageARB.StaticDraw);
        gl.BindBufferBase(BufferTargetARB.ShaderStorageBuffer, 0, _zeroStorageBuffer);
    }

    /// <summary>Resolves every distinct deferred pass EVERY placed actor needs to its real compiled program, once - not per frame, since none of this changes while the actor set stays the same. Call again whenever an actor is added/removed.</summary>
    public void SetScene(IReadOnlyList<LoadedModel> models)
    {
        _models = models;
        var allShapes = models.SelectMany(m => m.Shapes).ToList();
        _opaqueShapes = allShapes.Where(s => !s.Blend).ToList();
        var passNames = PassIdMaskPass.DistinctPasses(allShapes);
        foreach (string hostPass in _hostPasses)
            if (!passNames.Contains(hostPass))
                passNames = [.. passNames, hostPass];
        _cachedNeedsKnownMaterialFixes = _opaqueShapes.Any(KnownMaterialFixes.NeedsEyeVisibilityMaskFix);
        _mainShadowCache.SunWorld = null; // the scene's own bounds changed - invalidate the cached shadow map

        // Resolving reads every pass's material off disk and makes a buffer for it, so it is only
        // done when the set of passes changes - a host adding a map's models a few a frame calls
        // this every frame, and the passes settle after the first handful of models. The buffers
        // of the passes it replaces are freed; they used to be left behind on every call.
        if (passNames.SequenceEqual(_passNames, StringComparer.Ordinal) && _resolvedPasses.Count == passNames.Count)
            return;
        _passNames = passNames;
        foreach (var old in _resolvedPasses)
            _gl.DeleteBuffer(old.MaterialUboBuffer);
        _resolvedPasses = DeferredResolvePass.ResolveDeferredPasses(_gl, Programs, _decompiledDirectory, _deferredMaterialsDirectory, _passNames);
        Console.WriteLine($"  deferred passes: {string.Join(", ", _passNames)}");
    }

    /// <summary>Makes sure <paramref name="pass"/> is resolved, for terrain no actor's pass covers.</summary>
    void EnsurePass(string pass)
    {
        if (!_hostPasses.Contains(pass))
            _hostPasses.Add(pass);
        if (_passNames.Contains(pass))
            return;
        _passNames = [.. _passNames, pass];
        _resolvedPasses = [.. _resolvedPasses, .. DeferredResolvePass.ResolveDeferredPasses(_gl, Programs, _decompiledDirectory, _deferredMaterialsDirectory, [pass])
            .Select(p => p with { PassIndex = _passNames.Count - 1 })];
    }

    /// <summary>
    /// The host's water, through the game's water program (see <see cref="TerrainShading"/>'s water
    /// remarks) - drawn into the G-buffer over the lit opaque scene, or, when
    /// <paramref name="stamp"/>, marked in the pass-ID mask for <c>field_water</c>.
    /// </summary>
    void DrawTerrainWater(ITerrainHost host, RenderTargets targets, Camera camera, bool stamp)
    {
        if (!Terrain.BindWater())
            return;
        Resources.BindUbo("ctx_terrain", 1);
        Resources.BindUbo("env", 6);
        _gl.Disable(EnableCap.CullFace);
        _gl.Disable(EnableCap.Blend);
        var draw = new TerrainDraw(_gl, Hosting.YUpWorld.PointBack(camera.Eye), -1, default);
        if (!stamp)
        {
            targets.BindGBuffer();
            _gl.Enable(EnableCap.DepthTest);
            _gl.DepthFunc(DepthFunction.Lequal);
            _gl.DepthMask(true);
            BindUnit(SceneColorShapePass.MaterialIdUnit, targets.MaterialIdCopy.Handle);
            BindUnit(SceneColorShapePass.LinearDepthHalfUnit, targets.LinearDepthHalf.Handle);
            BindUnit(TerrainWaterColorBufferUnit, targets.Behind.Handle);
            ClipOrigin.Game(_gl, true);
            host.DrawWater(draw, stamp: false);
            ClipOrigin.Game(_gl, false);
            _gl.DepthFunc(DepthFunction.Less);
        }
        else
        {
            // Drawn the right way up, as the mask is, against the G-buffer's depth - see PassIdMaskPass.
            targets.BindPassIdTarget();
            _gl.Disable(EnableCap.DepthTest);
            int index = _passNames.IndexOf(WaterPass);
            var block = new float[8] { (index + 1) / 255f, camera.NearPlane, camera.FarPlane, 0f, 1f / targets.Width, 1f / targets.Height, 0f, 0f };
            Resources.Ubo("terrain_water_stamp", System.Runtime.InteropServices.MemoryMarshal.AsBytes(block.AsSpan()), bindingIndex: TerrainShading.StampBinding);
            BindUnit(TerrainShading.StampDepthUnit, targets.GBufferDepth.Handle);
            host.DrawWater(draw, stamp: true);
        }
        _gl.UseProgram(0);
        _gl.BindVertexArray(0);
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.Disable(EnableCap.DepthTest);
        GLDiagnostics.CheckPass(_gl, stamp ? "terrain water stamp" : "terrain water");
    }

    /// <summary><c>cTex_ColorBuffer</c>'s unit in the terrain water program, its own binding.</summary>
    const int TerrainWaterColorBufferUnit = 19;

    /// <summary>
    /// The terrain's G-buffer half, after the actors', as the game orders it: the linear depth and
    /// G-buffer under it are copied first, for the soft edge where the ground meets something set
    /// into it, then the host draws through the game's terrain program with a <c>Context</c> in the
    /// game's own Y-up world.
    /// </summary>
    void DrawTerrainGBuffer(ITerrainHost host, RenderTargets targets, Camera camera, Camera.ViewProjection vp,
        Vector4[] viewProjFlipped, Vector4[] projFlipped, Vector4[] viewInv4, Vector2 preTexel)
    {
        _linearDepth.Run(Resources, targets, camera.NearPlane, camera.FarPlane);
        var (underAlbedo, underNormal, underDepth) = targets.TerrainUnderCopies();
        // The depth under the terrain, for its soft edge - with nothing under it pushed out to
        // effectively infinity. The game's far plane is kilometres off; a host fits its own to the
        // scene, and with the sky only just past the ground the terrain judged itself to be lying
        // right on top of something and faded into the empty G-buffer under it: black ground.
        _underDepthProgram = _underDepthProgram != 0 ? _underDepthProgram
            : GLProgramBuilder.Build(_gl, UnderDepthVertex, UnderDepthFragment, "terrain_under_depth");
        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.Blend);
        targets.BindColorTarget(underDepth);
        _gl.UseProgram(_underDepthProgram);
        _gl.BindTextureUniform(_underDepthProgram, "t", 0, targets.LinearDepth.Handle);
        Resources.DrawFullscreenTriangle();
        _gl.CopyImageSubData(targets.GBuffer[1].Handle, CopyImageSubDataTarget.Texture2D, 0, 0, 0, 0,
            underAlbedo.Handle, CopyImageSubDataTarget.Texture2D, 0, 0, 0, 0, (uint)targets.Width, (uint)targets.Height, 1);
        _gl.CopyImageSubData(targets.GBuffer[3].Handle, CopyImageSubDataTarget.Texture2D, 0, 0, 0, 0,
            underNormal.Handle, CopyImageSubDataTarget.Texture2D, 0, 0, 0, 0, (uint)targets.Width, (uint)targets.Height, 1);

        var ctx = ContextUbo.BuildForCamera(TerrainShading.FromYUp(vp.View), TerrainShading.FromYUp(viewProjFlipped), projFlipped,
            TerrainShading.InverseToYUp(viewInv4[..3]), vp.Aspect, vp.TanHalfFovY, camera.NearPlane, camera.FarPlane, preTexel);
        Resources.Ubo("ctx_terrain", ctx.ToByteArray(), bindingIndex: 1);
        _gl.BindBufferBase(BufferTargetARB.UniformBuffer, 8, Terrain.MaterialBuffer);

        targets.BindGBuffer();
        targets.SetGBufferColorMask(true);
        _gl.Disable(EnableCap.Blend);
        _gl.Disable(EnableCap.CullFace);
        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Less);
        _gl.DepthMask(true);
        BindUnit(0, underAlbedo.Handle);
        BindUnit(1, underNormal.Handle);
        BindUnit(4, underDepth.Handle);

        ClipOrigin.Game(_gl, true);
        host.DrawGBuffer(new TerrainDraw(_gl, Hosting.YUpWorld.PointBack(camera.Eye), -1, default));
        ClipOrigin.Game(_gl, false);

        _gl.UseProgram(0);
        _gl.BindVertexArray(0);
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.Disable(EnableCap.DepthTest);
        GLDiagnostics.CheckPass(_gl, "terrain");
    }

    uint _underDepthProgram;

    const string UnderDepthVertex = """
        #version 450 core
        out vec2 vUV;
        void main()
        {
            float x = -1.0 + float((gl_VertexID & 1) * 4);
            float y = -1.0 + float((gl_VertexID & 2) * 2);
            vUV = vec2(x, y) * 0.5 + 0.5;
            gl_Position = vec4(x, y, 0.0, 1.0);
        }
        """;

    const string UnderDepthFragment = """
        #version 450 core
        uniform sampler2D t;
        in vec2 vUV;
        out float o;
        void main()
        {
            float d = texture(t, vUV).r;
            o = d >= 0.9999 ? 1.0e6 : d;
        }
        """;

    void BindUnit(int unit, uint handle)
    {
        _gl.ActiveTexture(TextureUnit.Texture0 + unit);
        _gl.BindTexture(TextureTarget.Texture2D, handle);
    }

    public void Resize(int width, int height) => Targets.Resize(width, height);

    /// <summary>
    /// Forces the shadow map to redraw on the next frame even though the sun/actor transforms
    /// haven't moved - needed whenever which SHAPES it should contain changes without either of
    /// those changing, e.g. the Model Config's "Enabled" toggle: without this the shadow-cache hit
    /// in <see cref="RenderFrame"/> would keep compositing a shadow from a shape that no longer
    /// draws (or skip one that just got re-enabled) until the camera or sun moved for an
    /// unrelated reason. Invalidates the MAIN view's cache by default; pass a secondary view's own
    /// <see cref="ShadowCache"/> (e.g. a picture-in-picture preview's) to invalidate that one
    /// instead - see <see cref="ShadowCache"/>'s remarks for why they're never shared.
    /// </summary>
    public void InvalidateShadowCache(ShadowCache? cache = null) => (cache ?? _mainShadowCache).SunWorld = null;

    /// <summary>
    /// Runs the real <c>agl::pfx::Sky</c> Bruneton precompute once, if it hasn't run yet.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="RenderFrame"/> on purpose. The bake depends only on the
    /// atmosphere's own authored parameters - not the camera, not the scene - so it must not be
    /// gated behind having a model placed. It originally sat inside <c>RenderFrame</c> and
    /// therefore never ran in an empty viewport, because the viewport skips <c>RenderFrame</c>
    /// entirely when no actor is placed (that method throws in that case). Idempotent: safe to
    /// call every frame, from anywhere with a current GL context.
    /// </remarks>
    public void EnsureSkyPrecomputed(Rendering.SkyPostFx postfx, EnvPalette? palette = null,
        string? paletteName = null, float tint = 0f)
    {
        // Re-bake when the palette's atmosphere changes. The LUT is solved FROM betaR/betaM, so a
        // palette that weights Rayleigh against Mie differently is a different-coloured sky - and
        // baking once meant every palette shared one atmosphere, which is why switching to
        // something like BloodyMoon changed brightness but never colour.
        // Keyed on the palette NAME, not on floats read back out of it. The bake is ~600 draw
        // calls (6 scattering orders x 16 slices x several passes), so it running even occasionally
        // per frame is catastrophic for framerate - a string compare makes "once per palette"
        // certain rather than dependent on float equality holding frame to frame.
        // The tint is baked INTO the LUT, so it is part of the bake identity. Quantised so that
        // dragging the slider does not fire a ~600-draw-call bake on every mouse delta.
        float tintStep = MathF.Round(Math.Clamp(tint, 0f, 1f) * 20f) / 20f;
        string key = $"{paletteName}|{tintStep:F2}";
        if (_skyPrecomputeDone && key == _skyPrecomputeKey)
            return;
        if (!_skyPrecompute.Available)
            return;

        // The amplifiers are real physics, and a palette may legitimately set Rayleigh to ZERO
        // (Prequel_BloodyMoon_Night has rayleigh 0.25; BloodyMoon_DarknessDragon has 0 with mie
        // 256) - which means "no blue sky", not "no sky". Taken literally that produces a LUT with
        // no Rayleigh term at all, so the palette's COLOUR cannot come from here: a red moon is red
        // because of BgDifColor/SkySunColor, which the tint below applies. Floored so the solve
        // still has something to integrate rather than collapsing.
        float rAmp = MathF.Max(0.02f, palette?.SkyRayleighAmplifier ?? 1f);
        float mAmp = palette is { SkyMieAmplifier: > 0f } ? palette.SkyMieAmplifier : 1f;
        _skyPrecomputeDone = true;
        _skyPrecomputeKey = key;
        Console.WriteLine($"[DeferredPipeline] baking sky LUT for palette '{key}' (rayleigh x{rAmp:G4}, mie x{mAmp:G4})");
        // BgDifColor is the palette's own background colour - confirmed against the real palette
        // files - so this is real authored data, not an invented tint.
        // FogColor's HUE, not BgDifColor. This is the difference between an orange sky and a red
        // one, and it is a property of the authored data rather than taste: BloodyMoon's BgDifColor
        // is (1.0, 0.297, 0.214) = #FF4C37, a coral - saturation 0.79 with green at 30%. No amount
        // of tinting toward an orange yields red. Its FogColor is (0.035, 0, 0.008), which is
        // #FF0039 once normalised - green exactly ZERO, saturation 1.0. That is the blood-moon red,
        // and it makes sense: with rayleigh 0 and mie 256 this palette's atmosphere IS dense fog,
        // so the fog colour is the sky colour.
        //
        // Normalised to its brightest channel because only the HUE is wanted - FogColor is authored
        // dark (0.035) and brightness comes from BgDifIntensity and the Sky HDR Level control.
        Vector3 fog = palette?.FogColor ?? Vector3.One;
        float fogMax = MathF.Max(fog.X, MathF.Max(fog.Y, fog.Z));
        Vector3 tintHue = fogMax > 1e-5f ? fog / fogMax : Vector3.One;
        Vector3 tintColor = Vector3.Lerp(Vector3.One, tintHue, tintStep);
        _skyPrecompute.Run(Resources, postfx, _skyPrecomputeSun, rAmp, mAmp, tintColor);
        GLDiagnostics.CheckPass(_gl, "sky precompute");
        _skyPrecompute.Verify();
    }

    /// <param name="targetsOverride">Render into a DIFFERENT <see cref="RenderTargets"/> than this pipeline's own <see cref="Targets"/> - e.g. a picture-in-picture camera preview's own small, permanently-allocated target set, so it never fights the main view for size (see ViewportPanel.RenderMiniCameraPreview's remarks on why resizing the shared Targets away and back every frame was a severe perf regression). Defaults to <see cref="Targets"/>.</param>
    /// <param name="shadowCacheOverride">The shadow-map reuse state to check/update for THIS render - defaults to the pipeline's own main-view cache. A second concurrently-visible view (rendering into its own <paramref name="targetsOverride"/>) must pass its OWN <see cref="ShadowCache"/> instance, never the main one - see that class's remarks.</param>
    public FrameResult RenderFrame(FrameRequest request, RenderTargets? targetsOverride = null, ShadowCache? shadowCacheOverride = null,
        GpuTexture? shadowMapOverride = null)
    {
        Timer.BeginFrame();
        ShapeDrawing.TakeCounts();
        ShadowCounts = default;
        // Safety net only - the real driver is ViewportPanel calling EnsureSkyPrecomputed right
        // after construction. Kept because it is idempotent and costs one bool test per frame,
        // so any future caller that renders without going through the panel still gets its LUTs.
        EnsureSkyPrecomputed(request.SkyPostFx ?? Rendering.SkyPostFx.Default, request.Palette,
            request.Lighting.PaletteName, request.Lighting.SkyPaletteTint);

        var instances = request.Instances ?? [];
        if (_models.Count == 0 || (request.Actors.Count == 0 && instances.Count == 0))
            throw new InvalidOperationException("No actors placed - call SetScene first.");
        var targets = targetsOverride ?? Targets;
        var shadowCache = shadowCacheOverride ?? _mainShadowCache;
        var camera = request.Camera;
        var lighting = request.Lighting;
        var pal = request.Palette;
        var skyPostFx = request.SkyPostFx ?? Rendering.SkyPostFx.Default;
        var cloudPostFx = request.CloudPostFx ?? Rendering.CloudPostFx.Default;
        var skyBin = request.SkyBin ?? Rendering.SkyBinLut.Empty;

        // Bracket the frame: anything pending here was raised outside it (by the UI, by a
        // resize, by an on-demand texture upload), and anything found at the end was raised by the
        // passes below. Without both, every GL error in the app surfaces at whichever unrelated
        // call site happens to drain the queue next.
        GLDiagnostics.CheckPending(_gl, "RenderFrame");

        // Rebound every frame rather than trusted from the constructor: a host drawing its own
        // scene between frames may well use storage binding 0 itself (see GLHostState).
        _gl.BindBufferBase(BufferTargetARB.ShaderStorageBuffer, 0, _zeroStorageBuffer);

        var vp = camera.BuildViewProjection(targets.Width, targets.Height);
        Vector4[] projFlipped = [vp.Proj[0], -vp.Proj[1], vp.Proj[2], vp.Proj[3]];
        Vector2 preTexel = new(1f / targets.Width, 1f / targets.Height);

        Vector3 sunWorld = SunWorldFromElevationAzimuth(lighting.SunElevation, lighting.SunAzimuth);
        Vector3 sunView = TransformDirection(vp.View, sunWorld);
        Vector3 sunColor = AmbientLighting.SunColor(pal);
        var (hemiSky, hemiGround) = AmbientLighting.ResolveHemisphereColors(pal, lighting.AmbientScale, skyPostFx);

        var viewMat4 = Mat4Math.ToMat4(vp.View);
        var viewInv4 = Mat4Math.Invert(viewMat4);
        var viewProj = Mat4Math.Multiply(vp.Proj, viewMat4);
        var viewProjFlipped = Mat4Math.Multiply(projFlipped, viewMat4);

        // Every Context a game program reads is in the game's own Y-up world (see GameWorld), while
        // the renderer's own passes keep its Z-up one.
        var gameView = GameWorld.Rows(vp.View);
        var gameViewInv = GameWorld.InverseRows(viewInv4[..3]);
        var ctxTrue = ContextUbo.BuildForCamera(gameView, GameWorld.Rows(viewProj), vp.Proj, gameViewInv, vp.Aspect, vp.TanHalfFovY, camera.NearPlane, camera.FarPlane, preTexel);
        // The G-buffer's orientation: the plain projection with the upper-left origin the game's
        // programs were written for where the driver has it (see ClipOrigin), else the flipped one.
        bool gameOrigin = ClipOrigin.Supported(_gl);
        var ctxGBuffer = gameOrigin
            ? ContextUbo.BuildForCamera(gameView, GameWorld.Rows(viewProj), vp.Proj, gameViewInv, vp.Aspect, vp.TanHalfFovY, camera.NearPlane, camera.FarPlane, preTexel)
            : ContextUbo.BuildForCamera(gameView, GameWorld.Rows(viewProjFlipped), projFlipped, gameViewInv, vp.Aspect, vp.TanHalfFovY, camera.NearPlane, camera.FarPlane, preTexel);
        // VolumeMaskColorNoUse is the palette's own "ignore my tint" switch (present on 131 of the
        // 131 shipped palettes, read by nothing until now). See EnvPalette.VolumeMaskColorNoUse for
        // why the tint it gates is inert in WildRenderingSharp either way.
        Vector3 volumeMaskColor = pal.VolumeMaskColorNoUse ? Vector3.Zero : pal.VolumeMaskColor;
        float volumeMaskIntensity = pal.VolumeMaskColorNoUse ? 0f : pal.VolumeMaskIntensity;
        var envUbo = EnvUbo.BuildFromLighting(sunView, GameWorld.Direction(sunWorld), sunColor, hemiSky, hemiGround, volumeMaskColor, volumeMaskIntensity, RenderTargets.ShadowMapSize);
        var sceneMatUbo = SceneMatUbo.BuildFromLighting(hemiSky, hemiGround, lighting.MidScale, lighting.HighlightScale);

        Resources.Ubo("ctx_true", ctxTrue.ToByteArray());
        Resources.Ubo("ctx_gbuffer", ctxGBuffer.ToByteArray(), bindingIndex: 1);
        Resources.Ubo("env", envUbo.ToByteArray(), bindingIndex: 6);
        Resources.Ubo("scenemat", sceneMatUbo.ToByteArray(), bindingIndex: 10);
        Resources.Ubo("support", SupportBufferUbo.Build(), bindingIndex: SupportBufferUbo.BindingIndex);
        Resources.BindZeroUbo(GlslSanitizer.OrphanBlockBinding, 65536);
        Resources.BindEngineVertexTextures();

        // Every placed actor's own draw group - bones/ShpMtx bytes built fresh this frame from
        // THAT actor's own transform/pose (never a shared/combined buffer - see class remarks),
        // and its own shapes filtered fresh every frame (not baked into _opaqueShapes/etc., which
        // are computed once in SetScene) so Model Config's "Enabled" toggle takes effect on the
        // very next frame, not just the next actor placed.
        var allGroups = request.Actors.Select(a => new ActorDrawGroup(
            BuildBonePalette(a.Model, GameWorld.PlacementRows(a.ModelMatrixRows), a.BoneWorldMatrices).ToByteArray(),
            ShapeMatrixUbo.BuildFromModelMatrix(GameWorld.PlacementRows(a.ModelMatrixRows)).ToByteArray(),
            GameWorld.PlacementRows(a.ModelMatrixRows),
            a.Model.Shapes.Where(s => s.Enabled && (!s.Hidden || s.CastsShadow)).ToList())).ToList();
        foreach (var batch in instances)
        {
            if (batch.Visible.Count > 0)
                allGroups.Add(new ActorDrawGroup([], [], IdentityRows,
                    batch.Model.Shapes.Where(s => s.Enabled && (!s.Hidden || s.CastsShadow) && (batch.IncludeBlended || (!s.Blend && !s.ForceForward))).ToList(), batch));
        }
        // Shapes that read the lit scene (water) draw after it is lit - see SceneColorShapePass.
        // What is seen: everything but shapes only there for their shadow (LoadedShape.Hidden).
        var castingGroups = allGroups;
        allGroups = [.. allGroups.Select(g => g with { Shapes = g.Shapes.Where(s => !s.Hidden).ToList() })];
        var opaqueGroups = allGroups
            .Select(g => g with { Shapes = g.Shapes.Where(s => !s.Blend && !s.ReadsSceneColor).ToList() })
            .Where(g => g.Shapes.Count > 0).ToList();

        // ---- G-buffer (flipped Context already bound at 1) ----
        ClipOrigin.Game(_gl, true);
        _gbuffer.Run(Resources, targets, opaqueGroups, Programs);
        ClipOrigin.Game(_gl, false);
        GLDiagnostics.CheckPass(_gl, "G-buffer pass");
        GBufferCounts = ShapeDrawing.TakeCounts();
        _terrainDrawn = false;
        if (request.Terrain is { } terrainHost && Terrain.Available)
        {
            _terrainDrawn = true;
            EnsurePass(DefaultPass);
            DrawTerrainGBuffer(terrainHost, targets, camera, vp,
                gameOrigin ? viewProj : viewProjFlipped, gameOrigin ? vp.Proj : projFlipped, viewInv4, preTexel);
        }
        Resources.BindUbo("ctx_true", 1); // every later pass uses the true (unflipped) projection

        // ---- shadow map (skipped when the camera is the only thing that moved - see ShadowCache's own remarks) ----
        // A posed actor misses the cache only when its pose actually CHANGED since the map was
        // drawn - compared bone for bone against a copy kept with the cache. This used to miss on
        // any posed actor at all, which a paused clip, helper bones or a host's external pose (every
        // rigged actor in Prism) all are, so a plain camera orbit around one redrew every shape
        // into the 4096x4096 map every frame for nothing. Comparing a couple of hundred matrices
        // is far cheaper than that.
        //
        // A frame-skipping throttle was tried here (only refresh every Nth animated frame, to cut
        // the sustained per-frame cost cloth/helper bones now force on this - the single most
        // expensive stage in the pipeline). It measurably reduced a driver/compositor-level frame-
        // timing glitch elsewhere, but traded it for a WORSE, directly visible regression: the
        // shadow visibly snaps/steps every time it refreshes instead of tracking smoothly, which
        // reads as "flickers every frame" during real animation - confirmed by the user immediately
        // after landing it. Reverted. Any real fix for the GPU-cost side of this has to make the
        // shadow pass itself cheaper, not skip drawing it while something is genuinely moving.
        if (request.ShadowFocus is { } shadowFocus)
            foreach (var batch in instances)
                batch.UpdateShadowRuns(shadowFocus);
        ScreenSpaceShadowAndAoPass.CascadeParams? cascadeParams = request.ShadowCascades is { Count: > 0 } cascades && shadowMapOverride is null
            ? RenderCascades(request, cascades, castingGroups, instances, sunWorld, camera, preTexel, targets, shadowCache)
            : null;

        var modelRowsPerActor = request.Actors.Select(a => a.ModelMatrixRows).ToArray();
        bool shadowCacheHit = cascadeParams is not null || shadowMapOverride is null && shadowCache.ModelRowsPerActor is not null && shadowCache.SunWorld == sunWorld
            && ActorRowsEqual(shadowCache.ModelRowsPerActor, modelRowsPerActor)
            && PosesEqual(shadowCache.BonesPerActor, request.Actors)
            && shadowCache.Focus == request.ShadowFocus
            && shadowCache.InstanceSignature == InstanceSignature(instances, request.ShadowFocus is not null);
        Vector3 rotatedLo, rotatedHi;
        ShadowPass.LightMatrices lightMatrices;
        if (cascadeParams is not null)
        {
            float r0 = shadowCache.CascadeRadius[0];
            rotatedLo = request.ShadowCascades![0].Center - new Vector3(r0);
            rotatedHi = request.ShadowCascades[0].Center + new Vector3(r0);
            lightMatrices = shadowCache.CascadeLight[0];
        }
        else if (shadowCacheHit)
        {
            rotatedLo = shadowCache.RotatedLo;
            rotatedHi = shadowCache.RotatedHi;
            lightMatrices = shadowCache.LightMatrices;
        }
        else
        {
            (rotatedLo, rotatedHi) = request.ShadowFocus is { } focus
                ? (focus.Center - new Vector3(focus.Radius), focus.Center + new Vector3(focus.Radius))
                : CombinedRotatedAabb(request.Actors, instances);
            lightMatrices = ShadowPass.BuildLightMatrices(rotatedLo, rotatedHi, sunWorld);
            if (shadowMapOverride is null)
            {
                var ctxLight = ContextUbo.BuildForCamera(GameWorld.Rows(lightMatrices.View3Rows), GameWorld.Rows(lightMatrices.ViewProj), lightMatrices.Proj,
                    GameWorld.InverseRows(Mat4Math.Invert(Mat4Math.ToMat4(lightMatrices.View3Rows))[..3]), 1f, 1f, camera.NearPlane, camera.FarPlane, preTexel);
                Resources.Ubo("ctx_light", ctxLight.ToByteArray(), bindingIndex: 1);
                _shadow.Run(Resources, targets, ShadowGroups(castingGroups, instances, request.ShadowFocus), Programs);
                GLDiagnostics.CheckPass(_gl, "shadow pass");
                Resources.BindUbo("ctx_true", 1);

                shadowCache.SunWorld = sunWorld;
                shadowCache.ModelRowsPerActor = modelRowsPerActor;
                // Copied: a host may well reuse and rewrite the same array next frame.
                shadowCache.BonesPerActor = request.Actors.Select(a => (Matrix4x4[]?)a.BoneWorldMatrices?.Clone()).ToArray();
                shadowCache.Focus = request.ShadowFocus;
                shadowCache.InstanceSignature = InstanceSignature(instances, request.ShadowFocus is not null);
                shadowCache.RotatedLo = rotatedLo;
                shadowCache.RotatedHi = rotatedHi;
                shadowCache.LightMatrices = lightMatrices;
            }
        }

        // ---- linear depth ----
        _linearDepth.Run(Resources, targets, camera.NearPlane, camera.FarPlane);
        GLDiagnostics.CheckPass(_gl, "linear depth pass");

        // ---- screen-space shadow + AO ----
        float lightRadius = (rotatedHi - rotatedLo).Length() * 0.5f + 1e-4f;
        var ssaoParams = new ScreenSpaceShadowAndAoPass.Params(
            ViewInv3Rows: viewInv4[..3], LightViewProj: lightMatrices.ViewProj,
            TanHalf: new Vector2(vp.Aspect * vp.TanHalfFovY, vp.TanHalfFovY),
            SunWorld: sunWorld, SunView: sunView,
            Near: camera.NearPlane, Far: camera.FarPlane,
            ShadowBias: request.ShadowBias, ShadowTexel: 1f / RenderTargets.ShadowMapSize,
            ShadowTexelWorld: (2f * lightRadius) / RenderTargets.ShadowMapSize, ShadowDepthRange: lightRadius * 5f - 0.01f,
            AoRadius: request.AoRadius, AoStrength: ScreenSpaceShadowAndAoPass.AoStrength,
            ShadowTexture: (shadowMapOverride ?? targets.ShadowMap).Handle,
            Cascades: cascadeParams);
        _shadowAo.Run(Resources, targets, ssaoParams);
        GLDiagnostics.CheckPass(_gl, "screen-space shadow/AO pass");

        // ---- light pre-pass (real per-pixel light colour for cTex_DeferredLightPrePass - see LightPrePass's own remarks) ----
        var lightPrePassParams = new LightPrePass.Params(
            ViewInv3Rows: viewInv4[..3], TanHalf: new Vector2(vp.Aspect * vp.TanHalfFovY, vp.TanHalfFovY),
            Near: camera.NearPlane, Far: camera.FarPlane,
            SunWorld: sunWorld, SunColor: sunColor, HemiSky: hemiSky, HemiGround: hemiGround);
        _lightPrePass.Run(Resources, targets, lightPrePassParams);
        GLDiagnostics.CheckPass(_gl, "light pre-pass");

        // ---- pass-ID mask (UNflipped proj - the resolve consumes it in its own true-GL space) ----
        // In the game's world, like the palettes and placements the mask draws with.
        var maskViewProj = GameWorld.Rows(Mat4Math.Multiply(vp.Proj, viewMat4));
        _passIdMask.Run(Resources, targets, allGroups, _passNames, maskViewProj, camera.NearPlane, camera.FarPlane);
        GLDiagnostics.CheckPass(_gl, "pass-ID mask");

        // ---- background (colour/transparent/real Rayleigh+Mie sky - see BackgroundPass) ----
        // Paints Final BEFORE the resolve loop below, which only ever composites into a pixel its
        // own pass-ID mask claims - see that pass's own remarks for why draw order (not blending)
        // is what makes this survive untouched everywhere no placed actor covers.
        var tanHalf = new Vector2(vp.Aspect * vp.TanHalfFovY, vp.TanHalfFovY);
        _background.Run(Resources, targets, lighting.Background, lighting.BackgroundColor, sunWorld, pal, viewInv4[..3], tanHalf, lighting.SceneGain, skyPostFx, cloudPostFx, skyBin, lighting.AtmosphereIntensity);
        GLDiagnostics.CheckPass(_gl, "background");

        // ---- the REAL sky: agl_sky_postfx_sky sampling the Bruneton LUT the precompute baked.
        // Overwrites what BackgroundPass just painted (its hand-written raymarch is the stand-in
        // this replaces), so it runs immediately after rather than instead of it - the background
        // pass also handles the non-TotkSky modes and clears Final.
        if (lighting.UseRealSkyShader && lighting.Background == BackgroundMode.TotkSky && _skyPostFx.Available)
        {
            // Anchored the same way BackgroundPass anchors its own sky, and for the same reason:
            // this paints into targets.Final AHEAD of TonemapPass's Exposure multiply, while the
            // LUT is in the game's own units (its RGB reaches ~60). Handing that over raw makes a
            // pure-white screen just as surely as the zero-RGB LUT made a black one.
            // BgDifIntensity is the palette's OWN sky brightness, and leaving it out meant every
            // palette rendered at exactly the same brightness - which is why a night palette came
            // out as bright as noon. That is also what turned BloodyMoon peach rather than dark
            // red: multiplying a too-bright LUT by a saturated red clips the red channel at 1 while
            // lifting green and blue, and over-exposed saturated red reads as peach.
            //
            // Normalised against the field's own default (5.0) rather than used raw, so palettes
            // near the default keep the brightness this pass was already calibrated at, and only
            // genuinely dark palettes get darker. BackgroundPass applies the same factor to its own
            // raymarch, so the two paths now agree.
            float paletteBrightness = pal.BgDifIntensity / 5.0f;
            // Aimed at an explicit HDR target rather than borrowed from the raymarch's constants.
            // BackgroundPass.SkyColorAnchor * SceneGain was calibrated against that pass's OWN
            // output magnitude, which has nothing to do with a normalised Bruneton LUT - using it
            // here put the sky at 0.28 and made it dark.
            //
            // The target is deliberately ABOVE 1. This is an HDR pipeline: the frame goes through
            // highlight compression and then the game's real agl_hdr_compose, which exist precisely
            // to bring values over 1 down to display range with their colour intact. Clamping the
            // sky under 1 to "avoid clipping" throws away the headroom the game itself uses - which
            // is what made every attempt either washed out (naive clipping) or flat (over-anchored).
            //
            // Because the LUT's peak is normalised at bake time, this lands consistently whatever a
            // palette's scattering amplifiers are, and the palette's own BgDifIntensity then makes
            // night palettes genuinely darker.
            float skyIntensity = lighting.AtmosphereIntensity * paletteBrightness
                * lighting.SkyHdrLevel
                / (SkyPrecomputePass.NormalisedPeak * MathF.Max(1e-4f, lighting.Exposure));
            // The shader computes mix(groundColour, lut.rgb * intensity, w) - the ground colour is
            // NOT scaled by intensity inside the shader, so it has to arrive pre-scaled or it goes
            // in raw and clips to WHITE under the horizon (1.0 * 9.5 exposure). That is the white
            // band below the horizon, and it is why it looked white rather than the palette's
            // colour: every channel was clipping, not just the brightest.
            Vector3 skyGroundColor = pal.BgDifColor * skyIntensity;
            // The horizon haze band (sky_postfx_sky's own USE_ADHOC_FOG path), driven by the
            // palette. Its colour is handed over in the SAME pre-scaled HDR units as the ground
            // colour above, because the shader mixes it in raw - it does not scale it by
            // Context[13].x the way it scales the LUT.
            var adhocFog = lighting.UseSkyFog
                ? SkyPostFxPass.Resolve(pal, skyPostFx, skyIntensity, lighting.SkyFogStrength, lighting.SkyFogNormaliseHue)
                : default;
            _skyPostFx.Run(Resources, targets, targets.Final, _skyPrecompute.BakedInscatter,
                viewInv4[..3], vp.Aspect, vp.TanHalfFovY, sunWorld, skyPostFx, skyIntensity, skyGroundColor, lighting.SkyPaletteTint,
                adhocFog);
            GLDiagnostics.CheckPass(_gl, "real sky postfx");
        }

        // ---- sun and moon, from the game's own sprites (SkyBodyPass) ----
        // After the sky so they sit on it, and BEFORE the cloud dome so cloud occludes them.
        if (lighting.Background == BackgroundMode.TotkSky && (lighting.ShowSun || lighting.ShowMoon))
        {
            var moonWorld = SunWorldFromElevationAzimuth(lighting.MoonElevation, lighting.MoonAzimuth);
            // The sun sprite is tinted by the palette's own authored sun colour, normalised to a
            // hue so the sprite's brightness is the slider's business rather than the palette's
            // raw intensity (which runs to 18 at noon and would clip the disc to flat white).
            Vector3 sunHue = pal.SkySunColorNoUse ? Vector3.One : AmbientLighting.NormaliseHue(pal.SkySunColor);
            _skyBody.Run(Resources, targets, targets.Final, viewInv4[..3], vp.Aspect, vp.TanHalfFovY,
                new SkyBodyPass.Params(
                    SunDirZUp: sunWorld,
                    MoonDirZUp: moonWorld,
                    SunColor: sunHue * lighting.SunSpriteIntensity,
                    MoonColor: Vector3.One * lighting.MoonSpriteIntensity,
                    SunAngularRadius: float.DegreesToRadians(lighting.SunAngularRadiusDegrees),
                    MoonAngularRadius: float.DegreesToRadians(lighting.MoonAngularRadiusDegrees),
                    MoonPhase: lighting.MoonPhase,
                    DrawSun: lighting.ShowSun,
                    DrawMoon: lighting.ShowMoon));
            GLDiagnostics.CheckPass(_gl, "sky bodies");
        }

        // ---- real agl_cloud shader, driven with ground-truth-confirmed UBO offsets - see
        // CloudDomePass's own remarks. Experimental: gated behind its own toggle since several
        // fields are still honest placeholders (real base/noise textures not located, scatter
        // texture not wired to the real sky yet).
        if (lighting.UseRealCloudDome && lighting.Background == BackgroundMode.TotkSky)
        {
            _cloudDome.Run(Resources, targets, pal, cloudPostFx.Shared, cloudPostFx.Layer0, vp.View, vp.Proj, camera.Eye, sunWorld, camera.FarPlane, lighting.SceneGain, lighting.CloudBrightness, lighting.Exposure, lighting.AnimateClouds,
                lighting.CloudFade, pal.FogColor, lighting.CloudResolutionScale,
                _skyPrecompute.BakedInscatter);
            GLDiagnostics.CheckPass(_gl, "cloud dome");
        }

        // ---- deferred resolve, ID-masked ----
        bool sceneColorShapes = SceneColorShapePass.Any(allGroups);
        var sceneColorPasses = sceneColorShapes
            ? allGroups.SelectMany(g => g.Shapes).Where(s => s.ReadsSceneColor).Select(s => s.DeferredPass).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
        // The host's water reads the lit scene too, and is lit by field_water.
        var waterHost = _terrainDrawn && request.Terrain is { HasWater: true } && Terrain.WaterAvailable ? request.Terrain : null;
        if (waterHost is not null)
        {
            EnsurePass(WaterPass);
            sceneColorPasses.Add(WaterPass);
        }
        bool secondPhase = sceneColorShapes || waterHost is not null;
        _resolve.SetEnvironmentColor(hemiSky);
        // The pass that also lights the terrain - which no actor stamps - in whichever half of the
        // frame it runs in. A scene-colour shape lit by the same pass (glass, say) holds it back to
        // the second half; claiming the terrain only in the first then left it unlit whenever such
        // a shape was on screen.
        int defaultPass = request.Terrain is not null ? _passNames.IndexOf(DefaultPass) : -1;
        _resolve.Run(Resources, targets, _resolvedPasses, lighting.EmissionScale, lighting.SceneGain, lighting.Exposure,
            secondPhase ? name => !sceneColorPasses.Contains(name) : null, defaultPass);
        GLDiagnostics.CheckPass(_gl, "deferred resolve");

        // ---- shapes that read the lit scene (water): drawn over a copy of it, then their pixels
        // lit and composited - see SceneColorShapePass. The screen-space inputs are re-derived
        // because the G-buffer under those pixels just changed.
        if (secondPhase)
        {
            float emissionUnits = lighting.Exposure / MathF.Max(1e-4f, lighting.EmissionScale);
            _sceneColorShapes.CopyInputs(Resources, targets, emissionUnits);
            if (sceneColorShapes)
            {
                Resources.BindUbo("ctx_gbuffer", 1);
                ClipOrigin.Game(_gl, true);
                _sceneColorShapes.Run(Resources, targets, allGroups, Programs);
                ClipOrigin.Game(_gl, false);
            }
            if (waterHost is not null)
                DrawTerrainWater(waterHost, targets, camera, stamp: false);
            Resources.BindUbo("ctx_true", 1);
            GLDiagnostics.CheckPass(_gl, "scene-colour shapes");

            _linearDepth.Run(Resources, targets, camera.NearPlane, camera.FarPlane);
            _shadowAo.Run(Resources, targets, ssaoParams);
            _lightPrePass.Run(Resources, targets, lightPrePassParams);
            _passIdMask.Run(Resources, targets, allGroups, _passNames, maskViewProj, camera.NearPlane, camera.FarPlane);
            if (waterHost is not null)
            {
                DrawTerrainWater(waterHost, targets, camera, stamp: true);
                Resources.BindUbo("ctx_true", 1);
            }
            _resolve.Run(Resources, targets, _resolvedPasses, lighting.EmissionScale, lighting.SceneGain, lighting.Exposure,
                sceneColorPasses.Contains, defaultPass);
            GLDiagnostics.CheckPass(_gl, "scene-colour shapes resolve");
        }

        // ---- ground reference grid ---- drawn into Scene, depth-tested against GBufferDepth, so
        // opaque geometry occludes it correctly - BEFORE the forward pass, so blended materials
        // still draw over it, and using viewProjFlipped (Scene's own Y-flipped space), not a
        // "true" unflipped camera - see GridPass's own remarks on why that specific matrix matters.
        //
        // Scene is NOT valid content at this point in the frame on its own - ForwardPass.Run
        // populates it by copying Final into it as the FIRST thing it does internally, then draws
        // into it, then copies it back to Final as the LAST thing it does - so a grid draw sitting
        // here without also doing that same copy-in/copy-out was drawing into stale/empty content
        // that ForwardPass's own copy-in then immediately overwrote a moment later. Same FlipInto
        // sandwich the known-material-fixes block below already uses for exactly this reason.
        // A world of instance batches can have no actors at all.
        float sceneRadius = MathF.Max(1f, request.Actors.Select(a => a.Model.BoundsRadius)
            .Concat(instances.Select(b => b.Model.BoundsRadius)).DefaultIfEmpty(1f).Max());
        if (lighting.ShowGrid)
        {
            _forward.FlipInto(Resources, targets, targets.Scene, targets.Final, flip: true);
            _grid.Run(targets, viewProjFlipped, camera.Eye, MathF.Max(20f, sceneRadius * 20f));
            _forward.FlipInto(Resources, targets, targets.Final, targets.Scene, flip: true);
            GLDiagnostics.CheckPass(_gl, "grid");
        }

        // ---- known material fixes ("WildRenderingSharp Related Improvements") ----
        // Cheap per-shape check BEFORE paying for the two full-screen flips below - those flips ran
        // unconditionally whenever the toggle was on (the common case, since it defaults to true),
        // even for the overwhelming majority of models that have no shape a known fix applies to at
        // all. At 2x supersampling that's two full-resolution shader passes wasted on literally
        // every frame of every model except the couple this fix actually targets.
        if (lighting.EnableKnownMaterialFixes && _cachedNeedsKnownMaterialFixes)
        {
            _forward.FlipInto(Resources, targets, targets.Scene, targets.Final, flip: true);
            _knownFixes.Run(Resources, targets, opaqueGroups, GameWorld.Rows(viewProjFlipped), lighting.EmissionScale, lighting.Exposure);
            _forward.FlipInto(Resources, targets, targets.Final, targets.Scene, flip: true);
            GLDiagnostics.CheckPass(_gl, "known material fixes");
        }

        // ---- forward pass: blended materials over the resolved scene ----
        _forward.Run(Resources, targets, allGroups, Programs);
        GLDiagnostics.CheckPass(_gl, "forward pass");
        Resources.BindUbo("ctx_true", 1);

        // One-shot exposure measurement, requested by the viewer. Taken HERE, on Final, because
        // this is the last moment the buffer is still pre-exposure HDR.
        if (_measureExposureRequested)
        {
            _measureExposureRequested = false;
            LastExposureMeasurement = ExposureMeter.Measure(targets, targets.Final);
            if (LastExposureMeasurement is { } m)
                Console.WriteLine($"[ExposureMeter] geometric-mean luminance {m.GeometricMeanLuminance:G4}, " +
                    $"max {m.MaxLuminance:G4}, {m.SampleCount} samples -> suggested Exposure {m.SuggestedExposure:G4} " +
                    $"(currently {lighting.Exposure:G4})");
            else
                Console.WriteLine("[ExposureMeter] nothing lit enough to measure.");
        }

        // ---- lens flare: the game's own flare_filter_flare, additively on the HDR buffer ----
        // Before exposure/tonemap on purpose - a flare is light the lens ADDS, so it belongs in
        // the same linear space as the rest of the frame rather than painted over the grade.
        if (lighting.UseLensFlare)
        {
            _lensFlare.Run(Resources, targets, targets.Final, new LensFlarePass.Params(
                Threshold: lighting.LensFlareThreshold,
                GhostSpacing: lighting.LensFlareGhostSpacing,
                HaloTint: Vector3.One,
                HaloRadius: lighting.LensFlareHaloRadius,
                Intensity: Vector3.One * lighting.LensFlareIntensity,
                Exposure: lighting.Exposure,
                SkyOnly: lighting.LensFlareSkyOnly));
            GLDiagnostics.CheckPass(_gl, "lens flare");
        }

        // ---- exposure -> highlight compression -> bloom -> hdr_compose ----
        var hdrCompressed = _tonemap.RunExposureAndCompress(Resources, targets, lighting.Exposure);
        GLDiagnostics.CheckPass(_gl, "exposure/compress");
        // The palette owns bloom's shape (threshold/clamp) and its authored strength; the viewer's
        // own Bloom Intensity multiplies that strength rather than replacing it, so the control is
        // live without discarding what the palette authored. BloomEnable is the palette's own
        // switch - two of the shipped palettes set it false, and it used to be read by nothing.
        float bloomIntensity = pal.BloomEnable ? pal.BloomIntensity * lighting.BloomIntensity : 0f;
        _bloom.Run(Resources, targets, hdrCompressed, pal.BloomThreshold, pal.BloomClampedLuminance, bloomIntensity);
        GLDiagnostics.CheckPass(_gl, "bloom");
        var hdrParams = HdrComposeParamsUbo.BuildDefault();
        _tonemap.RunHdrComposite(Resources, targets, _hdrComposeProgram, hdrCompressed, targets.Bloom, hdrParams.ToByteArray());

        // ---- the game's own final grade (agl::pfx::ColorCorrection), after hdr_compose.
        // WildRenderingSharp never applied this, so every side-by-side against a screenshot was comparing an
        // ungraded image to a graded one - master_field.baglccr alone carries saturation 1.175.
        // Grades Ldr in place through a scratch copy, since a texture cannot be its own sampler and
        // render target in one pass.
        var colorCorrection = request.ColorCorrection ?? ColorCorrectionPostFx.Default;
        if (_colorCorrection.Run(Resources, targets, targets.Ldr, colorCorrection))
            GLDiagnostics.CheckPass(_gl, "color correction");

        // ---- hover highlight (Material Inspector) - drawn LAST, straight into the final
        // tonemapped Ldr buffer, with no depth test at all: "above everything" means untouched by
        // grading/bloom and visible even through whatever occludes the hovered object, not just
        // another translucent layer earlier passes could wash out or hide behind geometry.
        if (request.Highlight is { } h && h.ActorIndex >= 0 && h.ActorIndex < request.Actors.Count
            && h.ShapeIndex >= 0 && h.ShapeIndex < request.Actors[h.ActorIndex].Model.Shapes.Count)
        {
            var owningActor = allGroups[h.ActorIndex];
            var shape = request.Actors[h.ActorIndex].Model.Shapes[h.ShapeIndex];
            var mvp = Mat4Math.Multiply(maskViewProj, Mat4Math.ToMat4(owningActor.ModelMatrixRows));
            _highlight.Draw(Resources, targets, owningActor, shape, mvp, maskViewProj, new Vector4(1f, 0.85f, 0.2f, 0.2f));
            GLDiagnostics.CheckPass(_gl, "highlight overlay");
        }

        GLDiagnostics.Check(_gl, "RenderFrame");

        Timer.Mark("post");
        return new FrameResult(targets.Ldr, targets.Final, targets.GBuffer[1], targets.GBuffer[3], targets.PreShadow, targets.PreMisc, targets.PassId);
    }

    /// <summary>Builds one actor's real gsys_skeleton palette (bind pose, or its own animated pose), or falls back to <c>FillIdentity</c> for a model with no skeleton at all - every placed actor gets its own, never a shared/combined one (see class remarks).</summary>
    static BonePaletteUbo BuildBonePalette(LoadedModel model, Vector4[] modelMatrixRows, Matrix4x4[]? boneWorldOverride)
    {
        if (model.Skeleton is not { } skel)
            return BonePaletteUbo.FillIdentity(modelMatrixRows);

        Matrix4x4[] boneWorld = boneWorldOverride ?? SkeletonPose.BindPoseWorldMatrices(skel);

        // modelMatrixRows is WildRenderingSharp's GPU "rows" convention (translation in each row's W - see
        // EulerRotation/Mat4Math), NOT a native row-vector Matrix4x4's own row layout (which
        // BonePaletteUbo.Build's multiplication needs) - transpose it the same way
        // SkeletonManifest.InverseModelMatricesAsMatrices does, or every bone's palette matrix
        // silently loses the actor's placement transform.
        var modelTransform = new Matrix4x4(
            modelMatrixRows[0].X, modelMatrixRows[1].X, modelMatrixRows[2].X, 0,
            modelMatrixRows[0].Y, modelMatrixRows[1].Y, modelMatrixRows[2].Y, 0,
            modelMatrixRows[0].Z, modelMatrixRows[1].Z, modelMatrixRows[2].Z, 0,
            modelMatrixRows[0].W, modelMatrixRows[1].W, modelMatrixRows[2].W, 1);

        // MatrixToBoneList is the COMBINED smooth+rigid slot table; the inverse-bind array's own
        // length is what tells Build where the rigid segment starts (see BonePaletteUbo's remarks).
        return BonePaletteUbo.Build(boneWorld, CollectionsMarshal.AsSpan(skel.MatrixToBoneList),
            skel.InverseModelMatricesAsMatrices(), modelTransform);
    }

    static Vector3 SunWorldFromElevationAzimuth(float elevation, float azimuth)
    {
        float ce = MathF.Cos(elevation);
        return new Vector3(ce * MathF.Cos(azimuth), ce * MathF.Sin(azimuth), MathF.Sin(elevation));
    }

    static Vector3 TransformDirection(ReadOnlySpan<Vector4> rows, Vector3 d) => new(
        rows[0].X * d.X + rows[0].Y * d.Y + rows[0].Z * d.Z,
        rows[1].X * d.X + rows[1].Y * d.Y + rows[1].Z * d.Z,
        rows[2].X * d.X + rows[2].Y * d.Y + rows[2].Z * d.Z);

    static Vector3 TransformPoint(ReadOnlySpan<Vector4> rows, Vector3 p) => new(
        rows[0].X * p.X + rows[0].Y * p.Y + rows[0].Z * p.Z + rows[0].W,
        rows[1].X * p.X + rows[1].Y * p.Y + rows[1].Z * p.Z + rows[1].W,
        rows[2].X * p.X + rows[2].Y * p.Y + rows[2].Z * p.Z + rows[2].W);

    static bool RowsEqual(Vector4[] a, Vector4[] b)
    {
        if (a.Length != b.Length)
            return false;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i])
                return false;
        }
        return true;
    }

    static bool ActorRowsEqual(Vector4[][] a, Vector4[][] b)
    {
        if (a.Length != b.Length)
            return false;
        for (int i = 0; i < a.Length; i++)
        {
            if (!RowsEqual(a[i], b[i]))
                return false;
        }
        return true;
    }

    /// <summary>True when every actor's pose is the one the cached shadow map was drawn with.</summary>
    static bool PosesEqual(Matrix4x4[]?[]? cached, IReadOnlyList<ActorRenderInput> actors)
    {
        if (cached is null || cached.Length != actors.Count)
            return false;
        for (int i = 0; i < cached.Length; i++)
        {
            var before = cached[i];
            var now = actors[i].BoneWorldMatrices;
            if (before is null || now is null)
            {
                if (before is not null || now is not null)
                    return false;
                continue;
            }
            if (!before.AsSpan().SequenceEqual(now))
                return false;
        }
        return true;
    }

    static (Vector3 Lo, Vector3 Hi) RotateAabb(Vector3 lo, Vector3 hi, ReadOnlySpan<Vector4> modelRows)
    {
        var rlo = new Vector3(float.MaxValue);
        var rhi = new Vector3(float.MinValue);
        for (int i = 0; i < 8; i++)
        {
            var corner = new Vector3(
                (i & 1) == 0 ? lo.X : hi.X,
                (i & 2) == 0 ? lo.Y : hi.Y,
                (i & 4) == 0 ? lo.Z : hi.Z);
            var t = TransformPoint(modelRows, corner);
            rlo = Vector3.Min(rlo, t);
            rhi = Vector3.Max(rhi, t);
        }
        return (rlo, rhi);
    }

    /// <summary>The union of every placed actor's own rotated bounding box - the shadow frustum has to cover every actor that should cast/receive a shadow, not just one.</summary>
    static (Vector3 Lo, Vector3 Hi) CombinedRotatedAabb(IReadOnlyList<ActorRenderInput> actors, IReadOnlyList<InstanceBatch> instances)
    {
        var lo = new Vector3(float.MaxValue);
        var hi = new Vector3(float.MinValue);
        foreach (var actor in actors)
        {
            var (actorLo, actorHi) = RotateAabb(actor.Model.BoundsMin, actor.Model.BoundsMax, actor.ModelMatrixRows);
            lo = Vector3.Min(lo, actorLo);
            hi = Vector3.Max(hi, actorHi);
        }
        foreach (var batch in instances)
        {
            if (batch.Count == 0)
                continue;
            lo = Vector3.Min(lo, batch.BoundsMin);
            hi = Vector3.Max(hi, batch.BoundsMax);
        }
        return (lo, hi);
    }

    static readonly Vector4[] IdentityRows = [new(1, 0, 0, 0), new(0, 1, 0, 0), new(0, 0, 1, 0)];

    /// <summary>What the batches will draw - which batches, and which runs of each at which level - for the shadow map's reuse check.</summary>
    /// <summary>
    /// Draws whichever cascades are out of date and returns what the shadow lookup needs to read
    /// them - see <see cref="FrameRequest.ShadowCascades"/>.
    /// </summary>
    ScreenSpaceShadowAndAoPass.CascadeParams RenderCascades(FrameRequest request, IReadOnlyList<ShadowFocus> cascades,
        List<ActorDrawGroup> allGroups, IReadOnlyList<InstanceBatch> instances, Vector3 sunWorld, Camera camera,
        Vector2 preTexel, RenderTargets targets, ShadowCache cache)
    {
        int n = Math.Min(cascades.Count, RenderTargets.MaxCascades);
        var viewProj = new Vector4[n][];
        var texelWorld = new float[n];
        var bias = new float[n];
        long actorSignature = ActorShadowSignature(request.Actors);
        bool drew = false;
        for (int c = 0; c < n; c++)
        {
            var focus = cascades[c];
            var lo = focus.Center - new Vector3(focus.Radius);
            var hi = focus.Center + new Vector3(focus.Radius);
            float lightRadius = (hi - lo).Length() * 0.5f + 1e-4f;
            var lm = ShadowPass.BuildLightMatrices(lo, hi, sunWorld);
            var right = new Vector3(lm.View3Rows[0].X, lm.View3Rows[0].Y, lm.View3Rows[0].Z);
            var up = new Vector3(lm.View3Rows[1].X, lm.View3Rows[1].Y, lm.View3Rows[1].Z);
            foreach (var batch in instances)
                batch.UpdateCascadeRuns(c, focus, right, up, lightRadius);

            var hash = new HashCode();
            hash.Add(sunWorld);
            hash.Add(focus);
            hash.Add(actorSignature);
            hash.Add(request.Terrain?.ShadowVersion ?? 0);
            hash.Add(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(targets));
            foreach (var batch in instances)
            {
                hash.Add(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(batch));
                foreach (var run in batch.CascadeRuns(c))
                    hash.Add(run);
            }
            long signature = hash.ToHashCode() | (1L << 40);

            if (cache.CascadeSignature[c] != signature)
            {
                var ctxLight = ContextUbo.BuildForCamera(GameWorld.Rows(lm.View3Rows), GameWorld.Rows(lm.ViewProj), lm.Proj,
                    GameWorld.InverseRows(Mat4Math.Invert(Mat4Math.ToMat4(lm.View3Rows))[..3]), 1f, 1f, camera.NearPlane, camera.FarPlane, preTexel);
                Resources.Ubo("ctx_light", ctxLight.ToByteArray(), bindingIndex: 1);

                static ActorDrawGroup Casting(ActorDrawGroup g) => g with { Shapes = g.Shapes.Where(s => !s.ReadsSceneColor).ToList() };
                var groups = allGroups.Where(g => g.Batch is null).Select(Casting).ToList();
                foreach (var batch in instances)
                {
                    if (batch.CascadeRuns(c).Count > 0)
                        groups.Add(new ActorDrawGroup([], [], IdentityRows,
                            batch.Model.Shapes.Where(s => s.Enabled && s.CastsShadow && !s.ReadsSceneColor && (batch.IncludeBlended || (!s.Blend && !s.ForceForward))).ToList(),
                            batch, ShadowRuns: true, Cascade: c));
                }
                _shadow.Run(Resources, targets, groups, Programs, c);
                if (request.Terrain is { } terrainHost && Terrain.Available)
                {
                    var ctxTerrain = ContextUbo.BuildForCamera(TerrainShading.FromYUp(lm.View3Rows), TerrainShading.FromYUp(lm.ViewProj), lm.Proj,
                        TerrainShading.InverseToYUp(Mat4Math.Invert(Mat4Math.ToMat4(lm.View3Rows))[..3]), 1f, 1f, camera.NearPlane, camera.FarPlane, preTexel);
                    Resources.Ubo("ctx_light_terrain", ctxTerrain.ToByteArray(), bindingIndex: 1);
                    terrainHost.DrawShadow(new TerrainDraw(_gl, Hosting.YUpWorld.PointBack(camera.Eye), c,
                        new Vector4(Hosting.YUpWorld.PointBack(focus.Center), focus.Radius)));
                    _gl.UseProgram(0);
                    _gl.BindVertexArray(0);
                }
                cache.CascadeSignature[c] = signature;
                cache.CascadeLight[c] = lm;
                drew = true;
            }
            cache.CascadeRadius[c] = focus.Radius;
            viewProj[c] = cache.CascadeLight[c].ViewProj;
            texelWorld[c] = 2f * lightRadius / RenderTargets.CascadeSize;
            bias[c] = request.ShadowBias / (lightRadius * 5f - 0.01f);
        }
        if (drew)
        {
            GLDiagnostics.CheckPass(_gl, "shadow cascades");
            ShadowCounts = ShapeDrawing.TakeCounts();
            Resources.BindUbo("ctx_true", 1);
        }
        return new ScreenSpaceShadowAndAoPass.CascadeParams(targets.ShadowCascades.Handle, viewProj, texelWorld, bias);
    }

    /// <summary>A hash of every placed actor's placement and pose - what makes a cascade's actor casters change.</summary>
    static long ActorShadowSignature(IReadOnlyList<ActorRenderInput> actors)
    {
        var hash = new HashCode();
        foreach (var a in actors)
        {
            foreach (var row in a.ModelMatrixRows)
                hash.Add(row);
            if (a.BoneWorldMatrices is { } bones)
                foreach (var m in bones)
                    hash.Add(m);
        }
        return hash.ToHashCode();
    }

    /// <summary>
    /// What the shadow map draws for this frame. With a focus, batches cast from their own
    /// shadow-focus runs (<see cref="InstanceBatch.ShadowVisible"/>) - including a batch the camera
    /// sees none of - instead of from what is on screen.
    /// </summary>
    static List<ActorDrawGroup> ShadowGroups(List<ActorDrawGroup> allGroups, IReadOnlyList<InstanceBatch> instances, ShadowFocus? focus)
    {
        // Shapes drawn over the lit scene (water) cast nothing.
        static ActorDrawGroup Casting(ActorDrawGroup g) => g with { Shapes = g.Shapes.Where(s => !s.ReadsSceneColor).ToList() };
        if (focus is null)
            return [.. allGroups.Select(Casting)];
        var groups = allGroups.Where(g => g.Batch is null).Select(Casting).ToList();
        foreach (var batch in instances)
        {
            if (batch.ShadowVisible.Count > 0)
                groups.Add(new ActorDrawGroup([], [], IdentityRows,
                    batch.Model.Shapes.Where(s => s.Enabled && s.CastsShadow && !s.ReadsSceneColor && (batch.IncludeBlended || (!s.Blend && !s.ForceForward))).ToList(), batch, ShadowRuns: true));
        }
        return groups;
    }

    /// <param name="shadowRuns">Signs the shadow-focus runs rather than the camera's - see <see cref="ShadowGroups"/>.</param>
    static long InstanceSignature(IReadOnlyList<InstanceBatch> instances, bool shadowRuns)
    {
        var hash = new HashCode();
        foreach (var batch in instances)
        {
            hash.Add(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(batch));
            foreach (var run in shadowRuns ? batch.ShadowVisible : batch.Visible)
                hash.Add(run);
        }
        return hash.ToHashCode();
    }

    public void Dispose()
    {
        foreach (var pass in _resolvedPasses)
            _gl.DeleteBuffer(pass.MaterialUboBuffer);
        _resolvedPasses = [];
        Programs.Dispose();
        Resources.Dispose();
        Targets.Dispose();
        _linearDepth.Dispose();
        _shadowAo.Dispose();
        _lightPrePass.Dispose();
        _passIdMask.Dispose();
        _background.Dispose();
        _cloudDome.Dispose();
        _skyPrecompute.Dispose();
        _skyPostFx.Dispose();
        _skyBody.Dispose();
        _lensFlare.Dispose();
        _colorCorrection.Dispose();
        _resolve.Dispose();
        Timer.Dispose();
        Terrain.Dispose();
        _sceneColorShapes.Dispose();
        _knownFixes.Dispose();
        _forward.Dispose();
        _grid.Dispose();
        _highlight.Dispose();
        _bloom.Dispose();
        _tonemap.Dispose();
        _gl.DeleteBuffer(_zeroStorageBuffer);
    }
}
