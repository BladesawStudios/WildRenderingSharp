using System.Numerics;
using KingRenderingSharp.Content.Assets;
using KingRenderingSharp.Content.Assets.Loading;
using KingRenderingSharp.Content.Assets.Textures;
using KingRenderingSharp.Content.Graphics;
using KingRenderingSharp.Content.Rendering;
using KingRenderingSharp.Content.Rendering.Cameras;
using KingRenderingSharp.Content.Rendering.Lighting;
using KingRenderingSharp.Core.Storage;
using KingRenderingSharp.Games.Botw;
using KingRenderingSharp.Gpu;
using KingRenderingSharp.Graph.Pipeline;
using KingRenderingSharp.Hosting;
using KingRenderingSharp.Hosting.Views;
using KingRenderingSharp.Scene;

namespace KingRenderingSharp.Snapshot;

/// <summary>A Breath of the Wild model drawn through the BotW profile of the deferred pipeline.</summary>
sealed class BotwScene : ISnapshotScene
{
    readonly GLHostState _host;
    readonly SceneView _view;
    readonly RenderActor _actor;
    readonly KingRenderingSharp.Content.Packs.ModelPack _modelPack;
    readonly LightingContext _lighting = new() { Background = BackgroundMode.Transparent };
    readonly BotwEnvironment _environment = new();
    ulong _frame;

    public BotwScene(GpuApi gl, CacheLayout cache, string model, int size)
    {
        _host = GLHostState.Enter(gl);
        var stores = KingRenderingSharp.Content.Packs.SystemPack.StoresOf(cache);
        var pipeline = new DeferredPipeline(gl, stores, size, size, new BotwProfile());
        // The model's pack stays open as long as the scene is drawn.
        _modelPack = new KingRenderingSharp.Content.Packs.ModelPack(cache.ModelPack(model), cache.ModelShaders(model), model);
        ((KingRenderingSharp.Core.Storage.LayeredContentFiles)stores.Shaders).Add(_modelPack.Programs);
        _view = new SceneView(gl, pipeline);
        var loader = new ModelLoader(gl, pipeline.Programs, new ExternalTextures(gl)) { SharedTextures = new SharedTextures(gl) };
        _actor = new RenderActor { Model = loader.Load(_modelPack.Source, enableKnownDecompilerCorrections: false), ModelName = model, Name = model };
        pipeline.SetScene([_actor.Model]);
    }

    public LoadedModel Model => _actor.Model;

    public (Vector3 Min, Vector3 Max) Bounds => (Model.BoundsMin, Model.BoundsMax);

    public byte[] Render(Camera camera, int width, int height)
    {
        var framing = SceneFramingCalculator.ForModelRadius(Model.BoundsRadius);
        for (int i = 0; i < 3; i++)
        {
            var inputs = RenderActor.BuildRenderInputs([_actor], 1f / 60f, ++_frame);
            _view.Render(new FrameRequest(camera, _lighting, _environment, inputs, framing.AoRadius, framing.ShadowBias), width, height);
        }

        return Matte(_view.ReadOutputRgba8(), width, height);
    }

    // The BotW profile draws its own flat sky, so the picture is cut out by flood-filling from the border over what matches the corner colour: the model's own pixels, which the fill cannot
    // reach from outside, keep their opacity.
    static byte[] Matte(byte[] rgba, int width, int height)
    {
        byte r = rgba[0], g = rgba[1], b = rgba[2];
        bool Sky(int at) => Math.Abs(rgba[at * 4] - r) <= 3 && Math.Abs(rgba[at * 4 + 1] - g) <= 3 && Math.Abs(rgba[at * 4 + 2] - b) <= 3;
        var seen = new bool[width * height];
        var queue = new Queue<int>();
        void Push(int x, int y)
        {
            int at = y * width + x;
            if ((uint)x >= (uint)width || (uint)y >= (uint)height || seen[at] || !Sky(at)) return;
            seen[at] = true;
            queue.Enqueue(at);
        }

        for (int x = 0; x < width; x++) { Push(x, 0); Push(x, height - 1); }
        for (int y = 0; y < height; y++) { Push(0, y); Push(width - 1, y); }
        while (queue.Count > 0)
        {
            int at = queue.Dequeue(), x = at % width, y = at / width;
            rgba[at * 4 + 3] = 0;
            Push(x + 1, y); Push(x - 1, y); Push(x, y + 1); Push(x, y - 1);
        }

        return rgba;
    }

    public void Dispose()
    {
        _host.Dispose();
        _modelPack.Dispose();
    }
}
