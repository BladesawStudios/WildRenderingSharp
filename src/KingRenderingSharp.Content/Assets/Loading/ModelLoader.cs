using KingRenderingSharp.Gpu;
using KingRenderingSharp.Content.Animation.Clips;
using KingRenderingSharp.Content.Assets.Manifests;
using KingRenderingSharp.Content.Assets.Textures;
using KingRenderingSharp.Content.Shaders;
using KingRenderingSharp.Content.Assets.Sources;

namespace KingRenderingSharp.Content.Assets.Loading;

/// <summary>Turns a <see cref="ModelManifest"/> into a ready-to-draw <see cref="LoadedModel"/>: GL buffers, VAOs, textures and material blocks for every shape.</summary>
internal sealed class ModelLoader(GpuApi gl, ShaderProgramCache programs, ExternalTextures? external = null)
{
    public SharedTextures? SharedTextures { get; init; }

    public bool CompactVertices { get; init; }

    // The largest a texture's top level may be, in pixels; 0 for no limit.
    public int TextureMaxSize { get; init; }

    public bool DeferVertexArrays { get; init; }

    /// <summary>Loads a prepared model folder, the way an older preparation wrote it.</summary>
    public LoadedModel Load(string directory, bool enableKnownDecompilerCorrections = true) =>
        Load(new DirectoryModelSource(directory), enableKnownDecompilerCorrections);

    public LoadedModel Load(IModelSource source, bool enableKnownDecompilerCorrections = true)
    {
        var manifest = source.Manifest;
        var textures = new TextureCache(gl, source, external, SharedTextures, TextureMaxSize);

        SkeletonManifest? skeleton = source.Skeleton;
        bool weightsInert = CompactVertices && VertexCompactor.SmoothPaletteIsIdentity(skeleton);

        var options = new ShapeBuildOptions(CompactVertices, DeferVertexArrays, enableKnownDecompilerCorrections);
        var builder = new ShapeBuilder(gl, programs, source, textures, manifest, options, weightsInert);
        var shapes = manifest.Shapes.Select(builder.Build).OfType<LoadedShape>().ToList();
        if (shapes.Count == 0)
            throw new InvalidOperationException($"Manifest '{source.Name}' resolved no drawable shapes.");

        return new LoadedModel(gl, textures)
        {
            Manifest = manifest,
            Shapes = shapes,
            BoundsMin = builder.Bounds.Min,
            BoundsMax = builder.Bounds.Max,
            Skeleton = skeleton,
            Source = source,
            AvailableAnims = source.SkeletalAnims,
            AvailableTexturePatternAnims = source.TexturePatternAnims,
            AvailableMaterialAnims = source.MaterialAnims,
            AvailableVisibilityAnims = source.VisibilityAnims,
            PendingVertexArrays = builder.PendingVertexArrays.Count > 0 ? builder.PendingVertexArrays : null,
        };
    }
}
