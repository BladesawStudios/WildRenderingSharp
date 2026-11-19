using KingRenderingSharp.Gpu;
using System.Numerics;
using KingRenderingSharp.Content.Assets;
using KingRenderingSharp.Content.Rendering.Cameras;
using KingRenderingSharp.Content.Rendering.Lighting;
using KingRenderingSharp.Core.Rom;
using KingRenderingSharp.Core.Rom.Games;
using KingRenderingSharp.Core.Storage;

namespace KingRenderingSharp.Snapshot;

/// <summary>A Tears of the Kingdom model drawn by the full renderer.</summary>
sealed class TotkScene : ISnapshotScene
{
    readonly LayeredRom _rom;
    readonly KingRenderer _renderer;
    readonly Scene.RenderActor _actor;

    public TotkScene(GpuApi gl, string romfs, CacheLayout cache, string model, int size)
    {
        _rom = TotkRom.Open(romfs);
        _renderer = new KingRenderer(gl, cache, _rom, initialWidth: size, initialHeight: size);
        _renderer.Lighting.Background = BackgroundMode.Transparent;
        _actor = _renderer.AddActor(model);
    }

    public LoadedModel Model => _actor.Model;

    public (Vector3 Min, Vector3 Max) Bounds => (Model.BoundsMin, Model.BoundsMax);

    public byte[] Render(Camera camera, int width, int height)
    {
        // The sky bake, shadow cache and exposure probe settle over the first frames.
        for (int i = 0; i < 4; i++) _renderer.Render(camera, width, height, 1f / 60f);
        return _renderer.View.ReadOutputRgba8();
    }

    public void Dispose()
    {
        _renderer.Dispose();
        _rom.Dispose();
    }
}
