using KingRenderingSharp.Content.Assets;
using KingRenderingSharp.Content.Assets.Loading;
using KingRenderingSharp.Content.Assets.Textures;
using KingRenderingSharp.Gpu;
using KingRenderingSharp.Graph.Pipeline.Drawing;
using KingRenderingSharp.Graph.Pipeline.Merged;
using KingRenderingSharp.Games.Totk.Atmosphere;
using KingRenderingSharp.Content.Shaders;
using KingRenderingSharp.Core.Storage;

namespace KingRenderingSharp.Hosting.Content;

/// <summary>Loads prepared models from a cache into GL objects, on the render thread or on a worker with the vertex arrays left for the render thread.</summary>
internal sealed class ModelFactory(GpuApi gl, ShaderProgramCache programs, CacheLayout cache, ExternalTextures externalTextures,
    SharedTextures sharedTextures, TotkSettings totk, MergedDrawing merged)
{
    public bool CompactVertices { get; set; }

    public int TextureMaxSize { get; set; }

    public LoadedModel Load(string resolvedModelName)
    {
        using var _ = GLHostState.Enter(gl);
        return NewLoader(deferVertexArrays: false).Load(cache.ModelDirectory(resolvedModelName), totk.EnableKnownMaterialFixes);
    }

    // Safe off the render thread: the instanced programs are linked here and the vertex arrays wait for the render thread to create them.
    public LoadedModel LoadOnWorker(string resolvedModelName)
    {
        var model = NewLoader(deferVertexArrays: true).Load(cache.ModelDirectory(resolvedModelName), totk.EnableKnownMaterialFixes);
        foreach (var shape in model.Shapes)
            ActorDrawGroup.EnsureInstancedPrograms(programs, shape);
        merged.Prepare(model);
        gl.Finish();
        return model;
    }

    ModelLoader NewLoader(bool deferVertexArrays) =>
        new(gl, programs, externalTextures)
        {
            CompactVertices = CompactVertices,
            TextureMaxSize = TextureMaxSize,
            SharedTextures = sharedTextures,
            DeferVertexArrays = deferVertexArrays,
        };
}
