using KingRenderingSharp.Content.Animation.Clips;
using KingRenderingSharp.Content.Assets.Manifests;
using KingRenderingSharp.Content.Assets.Materials;
using KingRenderingSharp.Core.Storage;

namespace KingRenderingSharp.Content.Assets.Sources;

/// <summary>A model read from the folder an older preparation wrote: JSON manifests and loose or bundled binary files.</summary>
internal sealed class DirectoryModelSource : IModelSource
{
    readonly string _directory;

    public DirectoryModelSource(string directory)
    {
        _directory = directory;
        Name = Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        Manifest = ModelManifest.Load(Path.Combine(directory, $"{Name}.manifest.json"));
        string skeleton = Path.Combine(directory, $"{Name}.skeleton.json");
        Skeleton = ModelFiles.FileExists(skeleton) ? SkeletonManifest.Load(skeleton) : null;
        SkeletalAnims = [.. SkeletalAnimManifest.ListAvailable(directory, Name)];
        TexturePatternAnims = [.. TexturePatternAnimManifest.ListAvailable(directory, Name)];
        MaterialAnims = [.. MaterialAnimManifest.ListAvailable(directory, Name)];
        VisibilityAnims = [.. VisibilityAnimManifest.ListAvailable(directory, Name)];
    }

    public string Name { get; }

    public ModelManifest Manifest { get; }

    public SkeletonManifest? Skeleton { get; }

    public IReadOnlyList<string> SkeletalAnims { get; }

    public IReadOnlyList<string> TexturePatternAnims { get; }

    public IReadOnlyList<string> MaterialAnims { get; }

    public IReadOnlyList<string> VisibilityAnims { get; }

    public bool Has(string file) => CacheFiles.Exists(Path.Combine(_directory, file));

    public byte[] Bytes(string file) => CacheFiles.ReadAllBytes(Path.Combine(_directory, file));

    public string? Options(string shapeName) => ModelFiles.TryReadText(Path.Combine(_directory, $"{shapeName}_options.txt"));

    public MaterialParamLayout? ParamLayout(string materialFile) => MaterialParamLayout.TryLoadBeside(_directory, materialFile);

    public SkeletalAnimManifest SkeletalAnim(string name) => SkeletalAnimManifest.Load(Path.Combine(_directory, $"{Name}.{name}.anim.json"));

    public TexturePatternAnimManifest TexturePatternAnim(string name) => TexturePatternAnimManifest.Load(Path.Combine(_directory, $"{Name}.{name}.texpat.json"));

    public MaterialAnimManifest MaterialAnim(string name) => MaterialAnimManifest.Load(Path.Combine(_directory, $"{Name}.{name}.matanim.json"));

    public VisibilityAnimManifest VisibilityAnim(string name) => VisibilityAnimManifest.Load(Path.Combine(_directory, $"{Name}.{name}.visanim.json"));
}
