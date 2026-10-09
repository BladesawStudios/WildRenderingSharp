using System.Numerics;
using Silk.NET.OpenGL;
using WildRenderingSharp.Assets;
using WildRenderingSharp.Assets.Baking;
using WildRenderingSharp.Assets.Textures;
using WildRenderingSharp.Gpu;
using WildRenderingSharp.Hosting.Content;
using WildRenderingSharp.Hosting.Views;
using WildRenderingSharp.Pipeline;
using WildRenderingSharp.Pipeline.Drawing;
using WildRenderingSharp.Pipeline.Shadows;
using WildRenderingSharp.Profiles.Totk;
using WildRenderingSharp.Profiles.Totk.Atmosphere;
using WildRenderingSharp.Profiles.Totk.Terrain;
using WildRenderingSharp.Rendering.Cameras;
using WildRenderingSharp.Rendering.Lighting;
using WildRenderingSharp.Rom;
using WildRenderingSharp.Scene;
using WildRenderingSharp.Storage;

namespace WildRenderingSharp;

/// <summary>
/// The whole live renderer in one object: a pipeline, the world it is lit by, the actors placed in it, and a view to draw them
/// into.
/// </summary>
public sealed class WildRenderer : IDisposable
{
    readonly GL _gl;
    readonly List<RenderActor> _actors = [];
    readonly ModelFactory _models;
    readonly BakeAttachment _bakes;

    public CacheLayout Cache { get; }
    public RenderEnvironment Environment { get; }
    public LightingContext Lighting { get; } = new();
    public TotkSettings Totk { get; } = new();
    public DeferredPipeline Pipeline { get; }

    public SceneView View { get; }

    public ExternalTextures ExternalTextures { get; }

    public SharedTextures SharedTextures { get; }

    public IReadOnlyList<RenderActor> Actors => _actors;

    public ulong FrameId { get; private set; }

    public float AoRadius { get; set; } = 0.035f;
    public float ShadowBias { get; set; } = 0.0015f;

    public (int ActorIndex, int ShapeIndex)? Highlight { get; set; }

    public WildRenderer(GL gl, CacheLayout cache, IRomAccess? rom, RenderEnvironment? environment = null, int initialWidth = 1280, int initialHeight = 720)
    {
        if (!cache.HasSystemAssets)
            throw new InvalidOperationException(
                $"The cache at '{cache.Root}' has no system assets yet. Run IModelPreparer.EnsureSystemAssetsAsync against the romfs first.");

        _gl = gl;
        Cache = cache;
        Environment = environment ?? RenderEnvironment.Load(rom);

        using (GLHostState.Enter(gl))
        {
            Pipeline = new DeferredPipeline(gl, cache.Root, cache.Shaders, initialWidth, initialHeight, new TotkProfile(),
                cache.DeferredMaterials, cache.SystemTextures);
            View = new SceneView(gl, Pipeline);
            ExternalTextures = new ExternalTextures(gl);
            SharedTextures = new SharedTextures(gl);
            _models = new ModelFactory(gl, Pipeline.Programs, cache, ExternalTextures, SharedTextures, Totk);
            _bakes = new BakeAttachment(gl, cache);
            // The atmosphere bake is scene-independent and about 600 draw calls, so it runs once here against the palette that will be used.
            Pipeline.PrepareEnvironment(Environment.Resolve(Totk));
        }
    }

    public bool CompactModelVertices
    {
        get => _models.CompactVertices;
        set => _models.CompactVertices = value;
    }

    public BakeLibrary Bakes => _bakes.Library;

    public IReadOnlyList<string> AttachBake(InstanceBatch batch, IReadOnlyList<ulong> hashes) => _bakes.Attach(batch, hashes);

    public void ApplyBake(InstanceBatch batch, BakeActor?[] perInstance) => _bakes.Apply(batch, perInstance);

    public LoadedModel LoadModelOnWorker(string resolvedModelName) => _models.LoadOnWorker(resolvedModelName);

    public (BakeActor?[] PerInstance, IReadOnlyList<string> Missing) FindBakes(IReadOnlyList<ulong> hashes) => _bakes.Find(hashes);

    public LoadedModel LoadModel(string resolvedModelName) => _models.Load(resolvedModelName);

    public RenderActor AddActor(string resolvedModelName)
    {
        var actor = new RenderActor { Model = LoadModel(resolvedModelName), ModelName = resolvedModelName, Name = resolvedModelName };
        AddActor(actor);
        return actor;
    }

    readonly List<InstanceBatch> _instances = [];

    public IReadOnlyList<InstanceBatch> Instances => _instances;

    public ShadowFocus? ShadowFocus { get; set; }

    public IReadOnlyList<ShadowFocus>? ShadowCascades { get; set; }

    public ITerrainHost? Terrain { get; set; }

    public InstanceBatch AddInstances(LoadedModel model, IReadOnlyList<Vector4[]> placements, bool updateScene = true)
    {
        using var _ = GLHostState.Enter(_gl);
        var batch = new InstanceBatch(_gl, model, placements);
        // Linked now, while the host is loading: a link that misses the program binary cache costs 100-500 ms and would hitch the first visible frame.
        foreach (var shape in model.Shapes)
            ActorDrawGroup.EnsureInstancedPrograms(Pipeline.Programs, shape);
        _instances.Add(batch);
        if (updateScene)
            SceneChanged();
        return batch;
    }

    public void RemoveInstances(InstanceBatch batch, bool disposeModel = true)
    {
        if (!_instances.Remove(batch))
            return;
        SceneChanged();
        using var _ = GLHostState.Enter(_gl);
        batch.Dispose();
        if (disposeModel && !_instances.Any(b => ReferenceEquals(b.Model, batch.Model)) && !_actors.Any(a => ReferenceEquals(a.Model, batch.Model)))
            batch.Model.Dispose();
    }

    public void ClearInstances()
    {
        var all = _instances.ToList();
        _instances.Clear();
        SceneChanged();
        using var _ = GLHostState.Enter(_gl);
        foreach (var batch in all)
            batch.Dispose();
        foreach (var model in all.Select(b => b.Model).Distinct())
        {
            if (!_actors.Any(a => ReferenceEquals(a.Model, model)))
                model.Dispose();
        }
    }

    public void AddActor(RenderActor actor)
    {
        _actors.Add(actor);
        SceneChanged();
    }

    public void RemoveActor(RenderActor actor, bool dispose = true)
    {
        if (!_actors.Remove(actor))
            return;
        SceneChanged();
        if (dispose)
        {
            using var _ = GLHostState.Enter(_gl);
            actor.Dispose();
        }
    }

    public void ClearActors(bool dispose = true)
    {
        var all = _actors.ToList();
        _actors.Clear();
        SceneChanged();
        if (dispose)
        {
            using var _ = GLHostState.Enter(_gl);
            foreach (var a in all)
                a.Dispose();
        }
    }

    public void SceneChanged()
    {
        using var _ = GLHostState.Enter(_gl);
        Pipeline.SetScene(_actors.Select(a => a.Model).Concat(_instances.Select(b => b.Model)).Distinct().ToList());
        Pipeline.InvalidateShadowCache();
    }

    public bool Advance(float deltaSeconds)
    {
        bool moved = false;
        foreach (var actor in _actors)
            moved |= actor.AdvanceAnimation(deltaSeconds);
        return moved;
    }

    public (Vector3 Center, float Radius) FrameFor(Camera camera)
    {
        var (center, radius) = RenderActor.CombinedBounds(_actors.Where(a => a.Visible));
        var framing = SceneFramingCalculator.ForModelRadius(radius);
        AoRadius = framing.AoRadius;
        ShadowBias = framing.ShadowBias;
        camera.NearPlane = framing.Near;
        camera.FarPlane = framing.Far;
        return (center, radius);
    }

    public FrameRequest BuildRequest(Camera camera, float deltaSeconds)
    {
        FrameId++;
        foreach (var actor in _actors)
            actor.ApplyMaterialAnimations();
        var inputs = RenderActor.BuildRenderInputs(_actors, deltaSeconds, FrameId);
        return new FrameRequest(camera, Lighting, Environment.Resolve(Totk, Terrain), inputs,
            AoRadius, ShadowBias, Highlight, _instances, ShadowFocus, ShadowCascades);
    }

    public uint? Render(Camera camera, int width, int height, float deltaSeconds)
    {
        // A host's ground is something to draw on its own: a cave, a sky island or a shrine has no placed actors.
        if (!_actors.Any(a => a.Visible) && !_instances.Any(b => b.Visible.Count > 0) && Terrain is null)
            return null;

        using var _ = GLHostState.Enter(_gl);
        var request = BuildRequest(camera, deltaSeconds);
        View.Render(request, width, height);
        return View.OutputTexture;
    }

    public void ReleaseTargets()
    {
        using var _ = GLHostState.Enter(_gl);
        View.ReleaseTargets();
    }

    public void Dispose()
    {
        using var _ = GLHostState.Enter(_gl);
        foreach (var actor in _actors)
            actor.Dispose();
        _actors.Clear();
        foreach (var batch in _instances)
            batch.Dispose();
        foreach (var model in _instances.Select(b => b.Model).Distinct())
            model.Dispose();
        _instances.Clear();
        View.Dispose();
        ExternalTextures.Dispose();
        SharedTextures.Dispose();
        _bakes.Dispose();
        Pipeline.Dispose();
    }
}
