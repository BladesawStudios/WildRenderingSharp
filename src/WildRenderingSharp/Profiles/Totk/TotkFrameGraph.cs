using WildRenderingSharp.Assets;
using WildRenderingSharp.Graphics;
using WildRenderingSharp.Graphics.Contracts;
using WildRenderingSharp.Pipeline;
using WildRenderingSharp.Pipeline.Frame;
using WildRenderingSharp.Pipeline.Passes;
using WildRenderingSharp.Profiles.Totk.Atmosphere;
using WildRenderingSharp.Profiles.Totk.Atmosphere.Palettes;
using WildRenderingSharp.Profiles.Totk.Deferred;
using WildRenderingSharp.Profiles.Totk.Deferred.PassIds;
using WildRenderingSharp.Profiles.Totk.Deferred.Resolve;
using WildRenderingSharp.Profiles.Totk.Sky;
using WildRenderingSharp.Profiles.Totk.Sky.Precompute;
using WildRenderingSharp.Profiles.Totk.Stages;
using WildRenderingSharp.Profiles.Totk.Terrain;

namespace WildRenderingSharp.Profiles.Totk;

/// <summary>TotK's frame: a deferred G-buffer and lighting chain, the game's sky, and a forward pass for blended materials.</summary>
public sealed class TotkFrameGraph : IFrameGraph, IDeferredDebug
{
    readonly DeferredScene _scene;
    readonly SkyBake _skyBake;
    readonly ResolveStage _resolve;
    readonly List<IFrameStage> _stages;
    readonly List<IDisposable> _owned = [];

    /// <summary>The game's terrain programs, for a host that hands its terrain over (<see cref="TotkEnvironment.Terrain"/>).</summary>
    public TerrainShading Terrain { get; }

    /// <summary>The game's programs for a crbin mesh, for a host that draws caves, sky islands and the like.</summary>
    public CaveShading Cave { get; }

    public TotkFrameGraph(StageServices services)
    {
        var linearDepth = Own(new LinearDepthPass(services.Gl));
        var forward = Own(new ForwardPass(services.Gl, services.Directories.SystemTextures));
        Terrain = Own(new TerrainShading(services.Gl, services.Directories.Decompiled, services.Profile.Bindings));
        Cave = Own(new CaveShading(services.Gl, services.Directories.Decompiled));
        _scene = Own(new DeferredScene(services.Gl, services.Programs, services.Directories));
        var stamper = Own(new PassIdStamper(services.Gl, services.Drawer, _scene));
        _skyBake = Own(new SkyBake(services));
        var terrain = new TerrainRenderer(services, Terrain, linearDepth, _scene);
        var screenSpaceLighting = Own(new ScreenSpaceLightingStage(services, linearDepth));
        _resolve = Own(new ResolveStage(services, _scene, terrain, screenSpaceLighting, stamper));

        _stages =
        [
            new FrameSetupStage(services),
            Own(new TotkFrameConstantsStage(services)),
            new GBufferStage(services, _scene, terrain),
            new ShadowStage(services, terrain),
            screenSpaceLighting,
            new PassIdMaskStage(services, stamper),
            Own(new SkyStage(services, _skyBake)),
            _resolve,
            new SnapshotStage(0),
            Own(new GridStage(services, forward)),
            Own(new KnownMaterialFixesStage(services, _scene, forward)),
            new ForwardStage(services, forward),
            new SnapshotStage(1),
            new ExposureMeasureStage(services),
            Own(new LensFlareStage(services)),
            new SnapshotStage(2),
            Own(new TonemapStage(services)),
            Own(new ColorCorrectionStage(services)),
            Own(new HighlightStage(services)),
        ];
    }

    T Own<T>(T item) where T : IDisposable
    {
        _owned.Add(item);
        return item;
    }

    public void SetScene(IReadOnlyList<LoadedModel> models) => _scene.Set(models);

    public IReadOnlyList<string> PassNames => _scene.PassNames;

    public int DebugResolvePass { get => _resolve.DebugPass; set => _resolve.DebugPass = value; }

    public void TraceResolvePass(string pass, string path) => _resolve.RequestTrace(pass, path);

    public void PrepareEnvironment(IFrameEnvironment environment)
    {
        var totk = (TotkEnvironment)environment;
        _skyBake.Ensure(totk.SkyPostFx, totk.Palette, totk.Settings.PaletteName, totk.Settings.SkyPaletteTint);
    }

    public void Run(FrameContext frame)
    {
        foreach (var stage in _stages)
            stage.Run(frame);
    }

    public void Dispose()
    {
        foreach (var item in Enumerable.Reverse(_owned))
            item.Dispose();
        _owned.Clear();
    }
}
