using Silk.NET.OpenGL;
using WildRenderingSharp.Assets;
using WildRenderingSharp.Gpu;
using WildRenderingSharp.Graphics.Contracts;
using WildRenderingSharp.Pipeline.Drawing;
using WildRenderingSharp.Pipeline.Frame;
using WildRenderingSharp.Pipeline.Resources;
using WildRenderingSharp.Pipeline.Shadows;
using WildRenderingSharp.Pipeline.Targets;
using WildRenderingSharp.Shaders;

namespace WildRenderingSharp.Pipeline;

/// <summary>Renders frames: owns the GL resources, render targets and shadow cache, and runs the stages its game profile defines.</summary>
public sealed class DeferredPipeline : IDisposable
{
    readonly GL _gl;
    readonly ExposureProbe _exposure = new();
    readonly ShadowCache _mainShadowCache = new();

    IReadOnlyList<LoadedModel> _models = [];

    internal IGameProfile Profile { get; }
    internal GLResourceCache Resources { get; }
    public RenderTargets Targets { get; }
    internal ShaderProgramCache Programs { get; }

    internal ShapeDrawer Drawer { get; }

    internal IFrameGraph Graph { get; }

    // The graph's debug hooks, or null when it has none.
    internal IDeferredDebug? Debug => Graph as IDeferredDebug;

    // Keeps a copy of the HDR frame after the deferred resolve, the forward pass and the lens flare, for the scene view's debug modes.
    public bool SnapshotStages { get; set; }

    public GpuPassTimer Timer { get; }

    public (long Triangles, long Instances) GBufferCounts { get; private set; }

    public (long Triangles, long Instances) ShadowCounts { get; private set; }

    public ExposureMeter.Result? LastExposureMeasurement => _exposure.Last;

    internal DeferredPipeline(GL gl, string dataDirectory, string decompiledDirectory, int width, int height,
        IGameProfile profile, string? deferredMaterialsDirectory = null, string? systemTexturesDirectory = null)
    {
        _gl = gl;
        Profile = profile;

        string cacheRoot = Path.GetDirectoryName(decompiledDirectory) ?? decompiledDirectory;
        var directories = new AssetDirectories(
            decompiledDirectory,
            deferredMaterialsDirectory ?? Path.Combine(cacheRoot, "_deferred_materials"),
            systemTexturesDirectory ?? Path.Combine(cacheRoot, "_system_textures"));
        GLProgramBuilder.UseBinaryCache(gl, Path.Combine(cacheRoot, "_glprograms"));

        Resources = new GLResourceCache(gl, Profile.Bindings);
        Targets = new RenderTargets(gl, width, height);
        Programs = new ShaderProgramCache(gl, decompiledDirectory, Profile.Bindings, Profile.ShaderSources);
        Drawer = new ShapeDrawer(gl, Programs);
        Timer = new GpuPassTimer(gl);

        Graph = Profile.CreateFrameGraph(new StageServices(gl, Profile, Resources, Programs, Drawer, _exposure, directories));
    }

    public void RequestExposureMeasurement() => _exposure.Request();

    public void SetScene(IReadOnlyList<LoadedModel> models)
    {
        _models = models;
        _mainShadowCache.SunWorld = null;
        Graph.SetScene(models);
    }

    public void Resize(int width, int height) => Targets.Resize(width, height);

    public void InvalidateShadowCache(ShadowCache? cache = null) => (cache ?? _mainShadowCache).SunWorld = null;

    public void PrepareEnvironment(IFrameEnvironment environment) =>
        Graph.PrepareEnvironment(environment);

    public FrameResult RenderFrame(FrameRequest request, RenderTargets? targetsOverride = null, ShadowCache? shadowCacheOverride = null,
        GpuTexture? shadowMapOverride = null)
    {
        Timer.BeginFrame();
        Drawer.TakeCounts();
        ShadowCounts = default;
        PrepareEnvironment(request.Environment);

        if ((_models.Count == 0 || (request.Actors.Count == 0 && (request.Instances ?? []).Count == 0)) && !request.Environment.HasOwnGeometry)
            throw new InvalidOperationException("No actors placed - call SetScene first.");

        var frame = new FrameContext(request, targetsOverride ?? Targets, shadowCacheOverride ?? _mainShadowCache, shadowMapOverride)
        {
            SnapshotStages = SnapshotStages,
        };

        // Errors pending here came from outside the frame; anything found afterwards came from its passes.
        GLDiagnostics.CheckPending(_gl, "RenderFrame");
        Graph.Run(frame);
        GLDiagnostics.Check(_gl, "RenderFrame");

        GBufferCounts = frame.Stats.GBuffer;
        ShadowCounts = frame.Stats.Shadow;
        Timer.Mark("post");
        return frame.Result;
    }

    public void Dispose()
    {
        Graph.Dispose();
        Drawer.Dispose();
        Programs.Dispose();
        Resources.Dispose();
        Targets.Dispose();
        Timer.Dispose();
    }
}
