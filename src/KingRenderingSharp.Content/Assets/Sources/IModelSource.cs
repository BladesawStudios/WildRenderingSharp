using KingRenderingSharp.Content.Animation.Clips;
using KingRenderingSharp.Content.Assets.Manifests;
using KingRenderingSharp.Content.Assets.Materials;

namespace KingRenderingSharp.Content.Assets.Sources;

/// <summary>Where one model's data is read from: its manifest, skeleton, animations, and the vertex, index, material and texture bytes its shapes name.</summary>
/// <remarks>The files a manifest names (<c>vertex_file</c>, <c>index_file</c>, <c>material_ubo</c>, a sampler's <c>file</c>) are keys to <see cref="Bytes"/>; a source holds them wherever it likes.</remarks>
internal interface IModelSource : IFileSource
{
    /// <summary>The model's name: its folder's name for a prepared folder, the pack's model name for a pack.</summary>
    string Name { get; }

    ModelManifest Manifest { get; }

    SkeletonManifest? Skeleton { get; }

    /// <summary>The shape's options file text, or null when it has none.</summary>
    string? Options(string shapeName);

    /// <summary>Where the material block's named parameters sit; null when the model has no layout for it.</summary>
    MaterialParamLayout? ParamLayout(string materialFile);

    IReadOnlyList<string> SkeletalAnims { get; }

    IReadOnlyList<string> TexturePatternAnims { get; }

    IReadOnlyList<string> MaterialAnims { get; }

    IReadOnlyList<string> VisibilityAnims { get; }

    SkeletalAnimManifest SkeletalAnim(string name);

    TexturePatternAnimManifest TexturePatternAnim(string name);

    MaterialAnimManifest MaterialAnim(string name);

    VisibilityAnimManifest VisibilityAnim(string name);
}
