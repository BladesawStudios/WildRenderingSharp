using Silk.NET.OpenGL;
using WildRenderingSharp.Assets;
using WildRenderingSharp.Assets.Baking;
using WildRenderingSharp.Gpu;
using WildRenderingSharp.Pipeline.Drawing;
using WildRenderingSharp.Storage;

namespace WildRenderingSharp.Hosting.Content;

/// <summary>Finds the baked lighting for a batch's instances and attaches it, so each draws with its own atlas.</summary>
public sealed class BakeAttachment(GL gl, CacheLayout cache) : IDisposable
{
    BakeLibrary? _library;

    public BakeLibrary Library => _library ?? Interlocked.CompareExchange(ref _library, new BakeLibrary(gl, cache.Bake), null) ?? _library!;

    public (BakeActor?[] PerInstance, IReadOnlyList<string> Missing) Find(IReadOnlyList<ulong> hashes)
    {
        var missing = Library.MissingTiles(hashes);
        return (hashes.Select(Library.Find).ToArray(), missing);
    }

    // Returns the tiles with no bake.
    public IReadOnlyList<string> Attach(InstanceBatch batch, IReadOnlyList<ulong> hashes)
    {
        using var _ = GLHostState.Enter(gl);
        var (perInstance, missing) = Find(hashes);
        Apply(batch, perInstance);
        return missing;
    }

    public void Apply(InstanceBatch batch, BakeActor?[] perInstance)
    {
        using var _ = GLHostState.Enter(gl);
        if (perInstance.FirstOrDefault(b => b is not null) is { } any)
        {
            foreach (var shape in batch.Model.Shapes)
                SetMaterialIndex(shape, any);
        }
        batch.SetBake(perInstance);
    }

    public void Dispose() => _library?.Dispose();

    // The shape's own bake region, if the bake has one, is selected by the material index its block carries.
    static void SetMaterialIndex(LoadedShape shape, BakeActor bake)
    {
        shape.HasBakeRegion = bake.MaterialIndexByName.ContainsKey(shape.Material);
        if (!bake.MaterialIndexByName.TryGetValue(shape.Material, out int index))
            return;
        var entry = shape.MaterialParams?.Uniforms.FirstOrDefault(u => u.Name == "gsys_material_id");
        if (entry is not null && entry.Offset + 4 <= shape.MaterialBlock.Authored.Length)
            shape.MaterialBlock.SetAuthored(entry.Offset, index);
    }
}
