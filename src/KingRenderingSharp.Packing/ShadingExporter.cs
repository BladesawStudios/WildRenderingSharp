using BfShaderSharp.Assets;
using BfShaderSharp.Pack;
using KingRenderingSharp.Core.Storage;

namespace KingRenderingSharp.Packing;

/// <summary>Writes the terrain, cave and depths programs a pack holds as the files the renderer reads from the shaders folder: each program's stages, the default material block and, for the depths, the immediate constants.</summary>
internal static class ShadingExporter
{
    public static int Export(AssetPack pack, CacheLayout layout)
    {
        Directory.CreateDirectory(layout.Shaders);
        int written = 0;
        foreach (ShadingModelImporter.Set set in ShadingModelImporter.Sets)
        {
            if (pack.GameData(ShadingModelImporter.MaterialEntry(set)) is not { } material) continue;
            AtomicFile.WriteAllBytes(Path.Combine(layout.Shaders, $"{set.Name}_gsys_material.bin"), material);
            written++;
            foreach (int program in set.Programs)
            {
                string name = $"{set.Name}_prog{program}";
                foreach (string part in new[] { "vert", "frag" })
                    if (pack.GameData(ShadingModelImporter.EntryName(set, program, part)) is { } glsl)
                    {
                        AtomicFile.WriteAllBytes(Path.Combine(layout.Shaders, $"{name}_extracted.{part}"), glsl);
                        written++;
                    }

                if (pack.GameData(ShadingModelImporter.EntryName(set, program, "immediates")) is { } immediates)
                {
                    AtomicFile.WriteAllBytes(Path.Combine(layout.Shaders, $"{name}_vp_c1.bin"), immediates);
                    written++;
                }
            }
        }

        return written;
    }
}
