using System.Text.Json;
using BfresLibrary;
using ShaderLibrary;
using ShaderLibrary.CompileTool;
using TexSharp;
using WildRenderingSharp.Logging;
using WildRenderingSharp.Rom;

namespace WildRenderingSharp.Preparation.Totk;

/// <summary>Exports the terrain water program, its material block and the textures it reads.</summary>
public static class TotkTerrainWater
{
    public const int Program = 98;
    public const string ProgramName = "terrain_water_prog98";
    public const string MaterialName = "TranslucentNear";
    public const string MaterialFile = "terrain_water_material.bin";
    public const string TexturesFile = "terrain_water_textures.json";

    static readonly string[] Textures = ["WaterAlb", "WaterNrm", "WaterEmm"];

    // A texture list that still names BC4 is stale, since the renderer reads the mask already decoded.
    public static bool IsExported(string shadersDir) =>
        File.Exists(Path.Combine(shadersDir, ProgramName + "_extracted.frag"))
        && File.Exists(Path.Combine(shadersDir, TexturesFile))
        && !File.ReadAllText(Path.Combine(shadersDir, TexturesFile)).Contains("BC4", StringComparison.Ordinal);

    public static void Export(IRomAccess rom, string shadersDir)
    {
        Directory.CreateDirectory(shadersDir);
        string archive = rom.Enumerate("Shader", "terrain_water.*bfsha*").First();
        var shadingModel = new BfshaFile(new MemoryStream(rom.ReadAllBytesNested(archive).ToArray())).ShaderModels["terrain_water"];

        TotkShaderExports.WriteProgram(shadingModel, Program, ProgramName, shadersDir);
        WriteMaterial(rom, shadingModel, shadersDir);
        WriteTextures(rom, shadersDir);
    }

    // The material's gsys_material, built against this shading model's own block layout.
    static void WriteMaterial(IRomAccess rom, ShaderModel shadingModel, string shadersDir)
    {
        var assets = new TotkAssets(rom);
        byte[] fres = assets.ReadModel("Terrain.TeraWater") ?? throw new FileNotFoundException("Model/Terrain.TeraWater.bfres.mc");
        var resFile = new ResFile(new MemoryStream(fres), false);
        Material material = resFile.Models[0].Materials.Values.First(m => m.Name == MaterialName);

        var block = shadingModel.UniformBlocks["gsys_material"];
        File.WriteAllBytes(Path.Combine(shadersDir, MaterialFile), BuildMaterialUbo.BuildMaterialBlock(block, material));
        BuildMaterialUbo.WriteParamLayout(block, material, Path.Combine(shadersDir, "terrain_water_material.params.json"));
    }

    // Every slice of each texture the program reads, mip 0, back to back; a BC4 mask is written decoded, as R8.
    static void WriteTextures(IRomAccess rom, string shadersDir)
    {
        var written = new List<object>();
        foreach (string name in Textures)
        {
            if (TotkTextures.Find(rom, name) is not { } path)
            {
                Log.Warning($"[ExportTerrainWater] texture not found: {name}");
                continue;
            }

            var texture = TotkTextures.LoadAllSlices(rom, path);
            bool mask = texture.Format.ToString().StartsWith("BC4", StringComparison.Ordinal);
            string format = mask ? "R8" : texture.Format.ToString();
            string file = $"terrain_water_{name}_{texture.Width}x{texture.Height}x{texture.ArrayCount}_{format}.bin";
            using (var stream = File.Create(Path.Combine(shadersDir, file)))
                foreach (var surface in texture.Surfaces)
                    stream.Write(mask ? DecodeMask(surface.Data, texture.Width, texture.Height) : surface.Data);
            written.Add(new { name, file, format, width = texture.Width, height = texture.Height, layers = texture.ArrayCount });
            Log.Info($"[ExportTerrainWater] {name}: {texture.Width}x{texture.Height}, {texture.ArrayCount} slice(s), {format}");
        }
        File.WriteAllText(Path.Combine(shadersDir, TexturesFile), JsonSerializer.Serialize(written));
    }

    static byte[] DecodeMask(byte[] blocks, int width, int height)
    {
        var decoded = new byte[width * height];
        TotkSystemTextures.RedChannel(TextureFormat.Bc4, blocks, width, height, decoded);
        return decoded;
    }
}
