using BfShaderSharp.Assets;
using BfShaderSharp.Pack;
using KingRenderingSharp.Core.Storage;

namespace KingRenderingSharp.Packing;

/// <summary>Writes the grass programs a pack holds as files of the grass store: a file for each kind and stage, as the importer stored it, and the default material block.</summary>
internal static class GrassExporter
{
    /// <summary>Returns the number of files written; none when the pack has no grass programs.</summary>
    public static int Export(AssetPack pack, SystemFolders layout)
    {
        if (pack.GameData(GrassShaderImporter.MaterialEntry) is not { } material) return 0;
        Directory.CreateDirectory(layout.GrassShaders);
        int written = 0;
        foreach ((string kind, _) in GrassShaderImporter.Kinds)
            foreach (string stage in GrassShaderImporter.Stages)
            {
                byte[] bytes = pack.GameData(GrassShaderImporter.EntryName(kind, stage)) ?? throw new InvalidDataException($"The pack has no grass {kind}{stage}");
                AtomicFile.WriteAllBytes(Path.Combine(layout.GrassShaders, $"{kind}{stage}.bin"), bytes);
                written++;
            }

        AtomicFile.WriteAllBytes(Path.Combine(layout.GrassShaders, "GrassMaterial.bin"), material);
        return written + 1;
    }
}
