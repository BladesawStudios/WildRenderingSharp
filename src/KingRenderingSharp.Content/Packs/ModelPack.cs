using BfShaderSharp.Pack;
using BfShaderSharp.Pack.Records;
using BfShaderSharp.Prepared;
using KingRenderingSharp.Content.Assets.Sources;

namespace KingRenderingSharp.Content.Packs;

/// <summary>A model's pack and the prepared archives of the shaders it uses, opened together; the source reads from them for as long as the model lives.</summary>
internal sealed class ModelPack : IDisposable
{
    readonly AssetPack _pack;
    readonly Dictionary<string, PreparedArchive> _archives = new(StringComparer.Ordinal);
    readonly PackTextureFiles _textures;

    public ModelPack(string packPath, string shaderDirectory, string modelName)
    {
        _pack = AssetPack.Open(packPath);
        try
        {
            if (Directory.Exists(shaderDirectory))
                foreach (string file in Directory.EnumerateFiles(shaderDirectory, "*.bfsp")) _archives[Path.GetFileNameWithoutExtension(file)] = PreparedArchive.Open(file);
            Programs = new PackPrograms(_archives);
            _textures = new PackTextureFiles(_pack);
            string[] stems = [.. AnimationStems(_pack)];
            Source = new PackModelSource(_pack, Programs, _textures, FindModel(_pack, modelName) ?? throw new FileNotFoundException($"The pack {packPath} has no model for '{modelName}'"), modelName, stems);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public PackPrograms Programs { get; }

    public IModelSource Source { get; } = null!;

    /// <summary>The model entry a name asks for: a file stem or <c>Stem.Model</c>; the entry is the stem, or <c>Stem/Model</c>.</summary>
    public static string? FindModel(AssetPack pack, string name)
    {
        string stem = name.Contains('.') ? name[..name.IndexOf('.')] : name;
        IEnumerable<string> models = pack.Of(PackEntryKind.Model).Select(e => e.Name);
        return models.FirstOrDefault(n => n == stem || n.StartsWith(stem + "/", StringComparison.Ordinal) && (name == stem || n.EndsWith("/" + name[(stem.Length + 1)..], StringComparison.Ordinal)))
            ?? models.FirstOrDefault();
    }

    // The pack was prepared with only the animations of the stems it was asked for, so the stems are the entries' first segments.
    static IEnumerable<string> AnimationStems(AssetPack pack) =>
        new[] { PackEntryKind.SkeletalAnim, PackEntryKind.MaterialAnim, PackEntryKind.VisibilityAnim }
            .SelectMany(kind => pack.Of(kind)).Select(e => e.Name).Where(n => n.Contains('/')).Select(n => n[..n.IndexOf('/')]).Distinct(StringComparer.Ordinal);

    public void Dispose()
    {
        _textures?.Dispose();
        foreach (PreparedArchive archive in _archives.Values) archive.Dispose();
        _pack.Dispose();
    }
}
