using Silk.NET.OpenGL;
using WildRenderingSharp.Assets;
using WildRenderingSharp.Graphics;
using WildRenderingSharp.Pipeline.Frame;
using WildRenderingSharp.Profiles.Totk;
using WildRenderingSharp.Rendering;

namespace WildRenderingSharp.Pipeline;

/// <summary>
/// Renders frames: owns the GL resources, render targets and shadow cache, and runs the stages
/// its game profile defines. Every placed actor goes through this one instance, sharing the
/// G-buffer and shadow map; each actor's skinning uniforms are rebound before its own draws
/// (see <see cref="ActorDrawGroup"/>).
/// </summary>
public sealed class DeferredPipeline : IDisposable
{
    readonly GL _gl;
    readonly ExposureProbe _exposure = new();
    readonly ShadowCache _mainShadowCache = new();

    IReadOnlyList<LoadedModel> _models = [];

    public IGameProfile Profile { get; }
    public GLResourceCache Resources { get; }
    public RenderTargets Targets { get; }
    public ShaderProgramCache Programs { get; }

    /// <summary>The profile's frame, for a host that needs something specific to its game.</summary>
    public IFrameGraph Graph { get; }

    public GpuPassTimer Timer { get; }

    /// <summary>What the last frame's instanced draws submitted to the G-buffer (prepass and main together).</summary>
    public (long Triangles, long Instances) GBufferCounts { get; private set; }

    /// <summary>What the last frame's instanced draws submitted to the shadow cascades - zero when they were reused.</summary>
    public (long Triangles, long Instances) ShadowCounts { get; private set; }

    /// <summary>The result of the last <see cref="RequestExposureMeasurement"/>, or null if none or nothing was measurable.</summary>
    public ExposureMeter.Result? LastExposureMeasurement => _exposure.Last;

    public DeferredPipeline(GL gl, string dataDirectory, string decompiledDirectory, int width, int height,
        string? deferredMaterialsDirectory = null, string? systemTexturesDirectory = null, IGameProfile? profile = null)
    {
        _gl = gl;
        Profile = profile ?? new TotkProfile();
        InstancingContract.Detect(gl);

        string cacheRoot = Path.GetDirectoryName(decompiledDirectory) ?? decompiledDirectory;
        var directories = new AssetDirectories(
            decompiledDirectory,
            deferredMaterialsDirectory ?? Path.Combine(cacheRoot, "_deferred_materials"),
            systemTexturesDirectory ?? Path.Combine(cacheRoot, "_system_textures"));
        GLProgramBuilder.BinaryCacheDirectory ??= Path.Combine(cacheRoot, "_glprograms");

        Resources = new GLResourceCache(gl, Profile.Bindings);
        Targets = new RenderTargets(gl, width, height);
        Programs = new ShaderProgramCache(gl, decompiledDirectory, Profile.Bindings, Profile.ShaderSources);
        Timer = new GpuPassTimer(gl);

        Graph = Profile.CreateFrameGraph(new FrameServices(gl, Profile, Resources, Programs, _exposure, directories));
    }

    /// <summary>Measures what exposure the scene needs on the next frame; see <see cref="ExposureMeter"/>.</summary>
    public void RequestExposureMeasurement() => _exposure.Request();

    /// <summary>Call whenever the set of loaded models changes.</summary>
    public void SetScene(IReadOnlyList<LoadedModel> models)
    {
        _models = models;
        _mainShadowCache.SunWorld = null;
        Graph.SetScene(models);
    }

    public void Resize(int width, int height) => Targets.Resize(width, height);

    /// <summary>
    /// Forces the shadow map to redraw on the next frame, for changes to which shapes cast that
    /// neither the sun nor the actors' transforms reflect. Pass a secondary view's own cache to
    /// invalidate that one instead of the main view's.
    /// </summary>
    public void InvalidateShadowCache(ShadowCache? cache = null) => (cache ?? _mainShadowCache).SunWorld = null;

    /// <summary>
    /// Does the work that depends only on the environment, never the camera or the scene. Idempotent
    /// and safe to call every frame from anywhere with a current GL context.
    /// </summary>
    public void PrepareEnvironment(IFrameEnvironment environment) =>
        Graph.PrepareEnvironment(environment);

    /// <param name="targetsOverride">Render into these targets instead of <see cref="Targets"/>, for a second view that keeps its own size.</param>
    /// <param name="shadowCacheOverride">The shadow reuse state for this render. A second view needs its own; never share the main one.</param>
    /// <param name="shadowMapOverride">A shadow map drawn elsewhere, used instead of drawing one.</param>
    public FrameResult RenderFrame(FrameRequest request, RenderTargets? targetsOverride = null, ShadowCache? shadowCacheOverride = null,
        GpuTexture? shadowMapOverride = null)
    {
        Timer.BeginFrame();
        ShapeDrawing.TakeCounts();
        ShadowCounts = default;
        PrepareEnvironment(request.Environment);

        if (_models.Count == 0 || (request.Actors.Count == 0 && (request.Instances ?? []).Count == 0))
            throw new InvalidOperationException("No actors placed - call SetScene first.");

        var frame = new FrameContext(request, targetsOverride ?? Targets, shadowCacheOverride ?? _mainShadowCache, shadowMapOverride);

        // Errors pending here came from outside the frame; anything found afterwards came from its passes.
        GLDiagnostics.CheckPending(_gl, "RenderFrame");
        Graph.Run(frame);
        GLDiagnostics.Check(_gl, "RenderFrame");

        GBufferCounts = frame.GBufferCounts;
        ShadowCounts = frame.ShadowCounts;
        Timer.Mark("post");
        return frame.Result;
    }

    public void Dispose()
    {
        Graph.Dispose();
        Programs.Dispose();
        Resources.Dispose();
        Targets.Dispose();
        Timer.Dispose();
    }
}
