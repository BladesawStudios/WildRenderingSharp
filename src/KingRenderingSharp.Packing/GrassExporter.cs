using System.Text;
using System.Text.Json;
using BfShaderSharp.Assets;
using BfShaderSharp.Pack;
using KingRenderingSharp.Core.Storage;

namespace KingRenderingSharp.Packing;

/// <summary>Writes the grass programs a pack holds as the folder the renderer reads: a JSON file for each kind and stage, and the default material block, in a folder named for a hash of the archive.</summary>
internal static class GrassExporter
{
    /// <summary>Returns the folder written, or null when the pack has no grass programs.</summary>
    public static string? Export(AssetPack pack, CacheLayout layout)
    {
        if (pack.GameData(GrassShaderImporter.ArchiveEntry) is not { } hash || pack.GameData(GrassShaderImporter.MaterialEntry) is not { } material) return null;
        string folder = Path.Combine(layout.GrassShaders, Encoding.ASCII.GetString(hash));
        Directory.CreateDirectory(folder);
        foreach ((string kind, _) in GrassShaderImporter.Kinds)
            foreach (string stage in GrassShaderImporter.Stages)
            {
                GrassShaderImporter.Stage read = GrassShaderImporter.Read(pack, kind, stage) ?? throw new InvalidDataException($"The pack has no grass {kind}{stage}");
                var json = new { glsl = read.Glsl, constants = read.Constants.ToArray(), blocks = read.Blocks, samplers = read.Samplers };
                AtomicFile.WriteAllText(Path.Combine(folder, $"{kind}{stage}.json"), JsonSerializer.Serialize(json));
            }

        AtomicFile.WriteAllBytes(Path.Combine(folder, "GrassMaterial.bin"), material);
        return folder;
    }
}
