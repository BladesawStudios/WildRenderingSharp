using Silk.NET.OpenGL;
using WildRenderingSharp.Assets;
using WildRenderingSharp.Assets.Loading;
using WildRenderingSharp.Assets.Textures;
using WildRenderingSharp.Gpu;
using WildRenderingSharp.Pipeline.Drawing;
using WildRenderingSharp.Profiles.Totk.Atmosphere;
using WildRenderingSharp.Shaders;
using WildRenderingSharp.Storage;

namespace WildRenderingSharp.Hosting.Content;

/// <summary>Loads prepared models from a cache into GL objects, on the render thread or on a worker with the vertex arrays left for the render thread.</summary>
internal sealed class ModelFactory(GL gl, ShaderProgramCache programs, CacheLayout cache, ExternalTextures externalTextures,
    SharedTextures sharedTextures, TotkSettings totk)
{
    public bool CompactVertices { get; set; }

    public LoadedModel Load(string resolvedModelName)
    {
        using var _ = GLHostState.Enter(gl);
        return NewLoader(resolvedModelName, deferVertexArrays: false).Load(resolvedModelName, totk.EnableKnownMaterialFixes);
    }

    // Safe off the render thread: the instanced programs are linked here and the vertex arrays wait for the render thread to create them.
    public LoadedModel LoadOnWorker(string resolvedModelName)
    {
        var model = NewLoader(resolvedModelName, deferVertexArrays: true).Load(resolvedModelName, totk.EnableKnownMaterialFixes);
        foreach (var shape in model.Shapes)
            ActorDrawGroup.EnsureInstancedPrograms(programs, shape);
        gl.Finish();
        return model;
    }

    ModelLoader NewLoader(string resolvedModelName, bool deferVertexArrays) =>
        new(gl, programs, cache.ModelDirectory(resolvedModelName), externalTextures)
        {
            CompactVertices = CompactVertices,
            SharedTextures = sharedTextures,
            DeferVertexArrays = deferVertexArrays,
        };
}
