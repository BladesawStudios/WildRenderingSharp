using WildRenderingSharp.Assets;
using WildRenderingSharp.Graphics;
using WildRenderingSharp.Profiles.Totk.Atmosphere;
using WildRenderingSharp.Pipeline;
using WildRenderingSharp.Pipeline.Frame;
using WildRenderingSharp.Profiles.Totk.Deferred;
using WildRenderingSharp.Profiles.Totk.Sky;
using WildRenderingSharp.Profiles.Totk.Stages;
using WildRenderingSharp.Profiles.Totk.Terrain;
using WildRenderingSharp.Rendering;

namespace WildRenderingSharp.Profiles.Totk;

/// <summary>TotK's frame: a deferred G-buffer and lighting chain, the game's sky, and a forward pass for blended materials.</summary>
public sealed class TotkFrameGraph : IFrameGraph
{
    readonly DeferredScene _scene;
    readonly SkyBake _skyBake;
    readonly List<IFrameStage> _stages;
    readonly List<IDisposable> _owned = [];

    public TotkFrameGraph(FrameServices services)
    {
        var linearDepth = Own(new LinearDepthPass(services.Gl));
        var forward = Own(new ForwardPass(services.Gl, services.Directories.SystemTextures));
        var passIdMask = Own(new PassIdMaskPass(services.Gl));
        var terrainShading = Own(new TerrainShading(services.Gl, services.Directories.Decompiled, services.Profile.Bindings));
        _scene = Own(new DeferredScene(services.Gl, services.Programs, services.Directories));
        _skyBake = Own(new SkyBake(services));
        var terrain = new TerrainRenderer(services, terrainShading, linearDepth, _scene);
        var screenSpaceLighting = Own(new ScreenSpaceLightingStage(services, linearDepth));

        _stages =
        [
            new FrameSetupStage(services),
            Own(new TotkFrameConstantsStage(services)),
            new GBufferStage(services, _scene, terrain),
            new ShadowStage(services, terrain),
            screenSpaceLighting,
            new PassIdMaskStage(services, passIdMask, _scene),
            Own(new SkyStage(services, _skyBake)),
            Own(new ResolveStage(services, _scene, terrain, screenSpaceLighting, passIdMask)),
            Own(new GridStage(services, forward)),
            Own(new KnownMaterialFixesStage(services, _scene, forward)),
            new ForwardStage(services, forward),
            new ExposureMeasureStage(services),
            Own(new LensFlareStage(services)),
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

    public void PrepareEnvironment(IFrameEnvironment environment, LightingContext lighting)
    {
        var totk = (TotkEnvironment)environment;
        _skyBake.Ensure(totk.SkyPostFx, totk.Palette, lighting.PaletteName, lighting.SkyPaletteTint);
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
