using BfShaderSharp.Assets;
using BfShaderSharp.Pack;
using KingRenderingSharp.Content.Packs;
using KingRenderingSharp.Core.Storage;

namespace KingRenderingSharp.Packing;

/// <summary>Writes the terrain water a pack holds as the files the renderer reads from the shaders store: its program, each texture it reads as the pack stored it, and the material block of the TranslucentNear material.</summary>
/// <remarks>The material is prepared as the model Terrain.TeraWater through the pack preparer, in a folder of its own that is removed afterwards.</remarks>
internal static class TerrainWaterExporter
{
    const string ProgramName = "terrain_water_prog98";
    const string Model = "Terrain.TeraWater";
    const string Material = "TranslucentNear";

    /// <summary>Returns the number of files written.</summary>
    public static int Export(AssetPack pack, string romfs, CacheLayout layout, Action<string> log)
    {
        if (pack.GameData(TerrainWaterImporter.EntryName("frag")) is null) return 0;
        Directory.CreateDirectory(layout.Shaders);
        int written = 0;
        foreach (string part in new[] { "vert", "frag" })
            if (pack.GameData(TerrainWaterImporter.EntryName(part)) is { } glsl)
            {
                AtomicFile.WriteAllBytes(Path.Combine(layout.Shaders, $"{ProgramName}_extracted.{part}"), glsl);
                written++;
            }

        foreach (string name in TerrainWaterImporter.Textures)
            if (pack.GameData(TerrainWaterImporter.EntryName(name)) is { } texture)
            {
                AtomicFile.WriteAllBytes(Path.Combine(layout.Shaders, $"terrain_water_{name}.bin"), texture);
                written++;
            }

        string work = Path.Combine(Path.GetTempPath(), "krs-water-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(work);
            string packPath = Path.Combine(work, "model.bfap"), shaders = Path.Combine(work, "shaders");
            PackPreparer.Prepare(romfs, Model, packPath, shaders, log, animations: false);
            using ModelPack opened = new(packPath, shaders, Model);
            var shape = opened.Source.Manifest.Shapes.FirstOrDefault(s => s.Material == Material) ?? throw new InvalidDataException($"{Model} has no material {Material}");
            File.WriteAllBytes(Path.Combine(layout.Shaders, "terrain_water_material.bin"), opened.Source.Bytes(shape.MaterialFile));
            written++;
        }
        finally
        {
            try { Directory.Delete(work, true); } catch (IOException) { }
        }

        return written;
    }
}
