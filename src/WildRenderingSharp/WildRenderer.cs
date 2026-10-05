using System.Numerics;
using Silk.NET.OpenGL;
using WildRenderingSharp.Assets;
using WildRenderingSharp.Hosting;
using WildRenderingSharp.Pipeline;
using WildRenderingSharp.Rendering;
using WildRenderingSharp.Scene;

namespace WildRenderingSharp;

/// <summary>
/// The whole live renderer in one object: a pipeline, the world it is lit by, the actors placed in
/// it, and a view to draw them into. The quickest way for a tool to show a prepared model the way
/// the game draws it.
/// </summary>
/// <remarks>
/// <para>Typical use, from a host with a GL 4.5-capable context:</para>
/// <code>
/// // Off the GL thread, once: the shared assets the pipeline needs, then a model.
/// IModelPreparer preparer = new OutOfProcessPreparer();   // or InProcessPreparer
/// await preparer.EnsureSystemAssetsAsync(romfs, CacheLayout.Default);
/// string model = await preparer.PrepareAsync(new PrepareRequest(romfs, "Npc_Zelda", CacheLayout.Default));
///
/// // On the GL thread.
/// var renderer = new WildRenderer(gl, CacheLayout.Default, romfs);
/// var actor = renderer.AddActor(model);
/// ...
/// renderer.Advance(deltaSeconds);
/// renderer.Render(camera, width, height, deltaSeconds);   // then draw renderer.View.OutputTexture
/// </code>
/// <para>
/// Everything here is also usable piecemeal - <see cref="DeferredPipeline"/>, <see cref="SceneView"/>,
/// <see cref="RenderActor"/>, <see cref="RenderEnvironment"/> - for a host that wants its own
/// arrangement (several views of one scene, its own actor type, its own frame loop).
/// </para>
/// <para>
/// <see cref="Render"/> and <see cref="LoadModel"/> run inside a <see cref="GLHostState"/>, so a
/// host that changed GL's global conventions (reversed depth through <c>glClipControl</c>, say)
/// gets its own state back afterwards untouched.
/// </para>
/// </remarks>
public sealed class WildRenderer : IDisposable
{
    readonly GL _gl;
    readonly List<RenderActor> _actors = [];

    public CacheLayout Cache { get; }
    public string? RomfsRoot { get; }
    public RenderEnvironment Environment { get; }
    public LightingContext Lighting { get; } = new();
    public SceneWindSystem Wind { get; } = new();
    public DeferredPipeline Pipeline { get; }

    /// <summary>The main view <see cref="Render"/> draws into.</summary>
    public SceneView View { get; }

    public IReadOnlyList<RenderActor> Actors => _actors;

    /// <summary>Incremented once per <see cref="Render"/> - see <see cref="RenderActor.EvaluatePosedSkeleton"/> for why physics needs it.</summary>
    public ulong FrameId { get; private set; }

    /// <summary>Screen-space AO radius and shadow bias, in world units. Set from <see cref="SceneFramingCalculator"/> by <see cref="FrameFor"/>.</summary>
    public float AoRadius { get; set; } = 0.035f;
    public float ShadowBias { get; set; } = 0.0015f;

    /// <summary>Which actor/shape to draw a flat highlight over, or null.</summary>
    public (int ActorIndex, int ShapeIndex)? Highlight { get; set; }

    /// <summary>
    /// Creates the pipeline and its view, loads the world from the romfs and bakes the sky. Needs
    /// the GL context current, and <paramref name="cache"/> to hold the system assets
    /// (<see cref="CacheLayout.HasSystemAssets"/>) - see <see cref="IModelPreparer.EnsureSystemAssetsAsync"/>.
    /// </summary>
    /// <param name="environment">An environment already loaded off the GL thread, or null to load one here.</param>
    public WildRenderer(GL gl, CacheLayout cache, string? romfsRoot, RenderEnvironment? environment = null, int initialWidth = 1280, int initialHeight = 720)
    {
        if (!cache.HasSystemAssets)
            throw new InvalidOperationException(
                $"The cache at '{cache.Root}' has no system assets yet. Run IModelPreparer.EnsureSystemAssetsAsync against the romfs first.");

        _gl = gl;
        Cache = cache;
        RomfsRoot = romfsRoot;
        Environment = environment ?? RenderEnvironment.Load(romfsRoot, cache);

        using (GLHostState.Enter(gl))
        {
            Pipeline = new DeferredPipeline(gl, cache.Root, cache.Shaders, initialWidth, initialHeight,
                cache.DeferredMaterials, cache.SystemTextures);
            View = new SceneView(gl, Pipeline);
            // The atmosphere bake is scene-independent and ~600 draw calls, so it runs once here
            // against the palette that will actually be used rather than inside the first frame.
            Pipeline.EnsureSkyPrecomputed(Environment.SkyPostFx, Environment.Palettes.Get(Lighting.PaletteName),
                Lighting.PaletteName, Lighting.SkyPaletteTint);
        }
    }

    /// <summary>Loads a prepared model from the cache. Needs the GL context current; compiles the model's shader programs, so it can take a moment for a large model.</summary>
    public LoadedModel LoadModel(string resolvedModelName)
    {
        using var _ = GLHostState.Enter(_gl);
        var loader = new ModelLoader(_gl, Pipeline.Programs, Cache.ModelDirectory(resolvedModelName));
        return loader.Load(resolvedModelName, enableKnownDecompilerCorrections: Lighting.EnableKnownMaterialFixes);
    }

    /// <summary>Loads a prepared model and places it, standing upright at the origin.</summary>
    public RenderActor AddActor(string resolvedModelName)
    {
        var actor = new RenderActor { Model = LoadModel(resolvedModelName), ModelName = resolvedModelName, Name = resolvedModelName };
        AddActor(actor);
        return actor;
    }

    public void AddActor(RenderActor actor)
    {
        _actors.Add(actor);
        SceneChanged();
    }

    /// <summary>Removes an actor, and disposes it (its model's GL objects) unless told not to.</summary>
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

    /// <summary>
    /// Re-resolves the deferred passes the placed actors need and drops the cached shadow map - call
    /// after changing which shapes of an actor are enabled, or anything else <see cref="AddActor(RenderActor)"/>
    /// would otherwise have caught.
    /// </summary>
    public void SceneChanged()
    {
        using var _ = GLHostState.Enter(_gl);
        Pipeline.SetScene(_actors.Select(a => a.Model).ToList());
        Pipeline.InvalidateShadowCache();
    }

    /// <summary>Advances every actor's animation and the wind. True if anything moved, i.e. the next frame will differ.</summary>
    public bool Advance(float deltaSeconds)
    {
        bool moved = Wind.Advance(deltaSeconds);
        foreach (var actor in _actors)
            moved |= actor.AdvanceAnimation(deltaSeconds);
        return moved;
    }

    /// <summary>Sizes the AO radius, shadow bias and the camera's clip planes for the placed actors, and returns the bounds it used.</summary>
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

    /// <summary>
    /// The pipeline's input for one frame through <paramref name="camera"/>: applies every actor's
    /// material/pattern animations to its model and poses its skeleton. Advances
    /// <see cref="FrameId"/>, so physics steps once per call.
    /// </summary>
    public FrameRequest BuildRequest(Camera camera, float deltaSeconds)
    {
        FrameId++;
        foreach (var actor in _actors)
            actor.ApplyMaterialAnimations(_gl);
        var inputs = RenderActor.BuildRenderInputs(_actors, deltaSeconds, FrameId, Wind.CurrentForce);
        return new FrameRequest(camera, Lighting, Environment.Palettes.Get(Lighting.PaletteName), inputs,
            AoRadius, ShadowBias, Highlight, Environment.SkyPostFx, Environment.CloudPostFx, Environment.SkyBin,
            Environment.ColorCorrection);
    }

    /// <summary>
    /// Renders one frame into <see cref="View"/> and returns its output texture, or null when no
    /// visible actor is placed (the pipeline draws nothing without one).
    /// </summary>
    public uint? Render(Camera camera, int width, int height, float deltaSeconds)
    {
        if (!_actors.Any(a => a.Visible))
            return null;

        using var _ = GLHostState.Enter(_gl);
        var request = BuildRequest(camera, deltaSeconds);
        View.Render(request, width, height);
        return View.OutputTexture;
    }

    public void Dispose()
    {
        using var _ = GLHostState.Enter(_gl);
        foreach (var actor in _actors)
            actor.Dispose();
        _actors.Clear();
        View.Dispose();
        Pipeline.Dispose();
    }
}
