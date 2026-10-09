using Silk.NET.OpenGL;
using WildRenderingSharp.Animation.Clips;
using WildRenderingSharp.Assets.Manifests;
using WildRenderingSharp.Assets.Textures;
using WildRenderingSharp.Pipeline;
using WildRenderingSharp.Pipeline.Gpu;

namespace WildRenderingSharp.Assets.Loading;

/// <summary>Turns a <see cref="ModelManifest"/> into a ready-to-draw <see cref="LoadedModel"/>: GL buffers, VAOs, textures and material blocks for every shape.</summary>
public sealed class ModelLoader(GL gl, ShaderProgramCache programs, string dataDirectory, ExternalTextures? external = null)
{
    public SharedTextures? SharedTextures { get; init; }

    public bool CompactVertices { get; init; }

    public bool DeferVertexArrays { get; init; }

    public LoadedModel Load(string modelName, bool enableKnownDecompilerCorrections = true)
    {
        var manifest = ModelManifest.Load(Path.Combine(dataDirectory, $"{modelName}.manifest.json"));
        var textures = new TextureCache(gl, dataDirectory, external, SharedTextures);

        string skeletonPath = Path.Combine(dataDirectory, $"{modelName}.skeleton.json");
        SkeletonManifest? skeleton = File.Exists(skeletonPath) ? SkeletonManifest.Load(skeletonPath) : null;
        bool weightsInert = CompactVertices && VertexCompactor.SmoothPaletteIsIdentity(skeleton);

        var options = new ShapeBuildOptions(CompactVertices, DeferVertexArrays, enableKnownDecompilerCorrections);
        var builder = new ShapeBuilder(gl, programs, dataDirectory, textures, manifest, options, weightsInert);
        var shapes = manifest.Shapes.Select(builder.Build).OfType<LoadedShape>().ToList();
        if (shapes.Count == 0)
            throw new InvalidOperationException($"Manifest '{modelName}' resolved no drawable shapes.");

        return new LoadedModel(gl, textures)
        {
            Manifest = manifest,
            Shapes = shapes,
            BoundsMin = builder.Bounds.Min,
            BoundsMax = builder.Bounds.Max,
            VertexPositions = builder.Bounds.Positions,
            Skeleton = skeleton,
            DataDirectory = dataDirectory,
            AvailableAnims = SkeletalAnimManifest.ListAvailable(dataDirectory, modelName).ToList(),
            AvailableTexturePatternAnims = TexturePatternAnimManifest.ListAvailable(dataDirectory, modelName).ToList(),
            AvailableMaterialAnims = MaterialAnimManifest.ListAvailable(dataDirectory, modelName).ToList(),
            PendingVertexArrays = builder.PendingVertexArrays.Count > 0 ? builder.PendingVertexArrays : null,
        };
    }
}
