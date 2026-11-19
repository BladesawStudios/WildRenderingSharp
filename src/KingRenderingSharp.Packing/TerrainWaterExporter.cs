using System.Text.Json;
using BfShaderSharp.Assets;
using BfShaderSharp.Pack;
using KingRenderingSharp.Core.Storage;

namespace KingRenderingSharp.Packing;

/// <summary>Writes the terrain water a pack holds as the files the renderer reads from the shaders folder: its program, the textures it reads with their list, and the material block and parameter layout of the TranslucentNear material.</summary>
/// <remarks>The material is prepared as the model Terrain.TeraWater through the pack preparer, in a folder of its own that is removed afterwards.</remarks>
internal static class TerrainWaterExporter
{
    const string ProgramName = "terrain_water_prog98";
    const string Model = "Terrain.TeraWater";
    const string Material = "TranslucentNear";

    /// <summary>Returns the number of files written.</summary>
    public static int Export(AssetPack pack, string romfs, CacheLayout layout, bool programs, Action<string> log)
    {
        if (pack.GameData(TerrainWaterImporter.EntryName("frag")) is null) return 0;
        Directory.CreateDirectory(layout.Shaders);
        int written = 0;
        foreach (string part in programs ? new[] { "vert", "frag" } : [])
            if (pack.GameData(TerrainWaterImporter.EntryName(part)) is { } glsl)
            {
                AtomicFile.WriteAllBytes(Path.Combine(layout.Shaders, $"{ProgramName}_extracted.{part}"), glsl);
                written++;
            }

        List<object> listed = [];
        foreach (string name in TerrainWaterImporter.Textures)
        {
            if (TerrainWaterImporter.Read(pack, name) is not { } texture) continue;
            string file = $"terrain_water_{name}_{texture.Width}x{texture.Height}x{texture.Layers}_{texture.Format}.bin";
            File.WriteAllBytes(Path.Combine(layout.Shaders, file), texture.Bytes);
            listed.Add(new { name, file, format = texture.Format, width = texture.Width, height = texture.Height, layers = texture.Layers });
            written++;
        }

        File.WriteAllText(Path.Combine(layout.Shaders, "terrain_water_textures.json"), JsonSerializer.Serialize(listed));
        string work = Path.Combine(Path.GetTempPath(), "krs-water-" + Guid.NewGuid().ToString("N"));
        try
        {
            PackPreparer.Prepare(romfs, Model, Path.Combine(work, "model"), Path.Combine(work, "shaders"), log, animations: false);
            string matubo = Path.Combine(work, "model", "matubo");
            File.Copy(Path.Combine(matubo, $"{Material}.gsys_material.bin"), Path.Combine(layout.Shaders, "terrain_water_material.bin"), true);
            written++;
            if (programs)
            {
                File.Copy(Path.Combine(matubo, $"{Material}.params.json"), Path.Combine(layout.Shaders, "terrain_water_material.params.json"), true);
                written++;
            }
        }
        finally
        {
            try { Directory.Delete(work, true); } catch (IOException) { }
        }

        return written;
    }
}
