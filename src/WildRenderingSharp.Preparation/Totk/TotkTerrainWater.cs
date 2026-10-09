using System.Text.Json;
using BfresLibrary;
using ShaderLibrary;
using ShaderLibrary.CompileTool;
using WildRenderingSharp.Rom;

namespace WildRenderingSharp.Preparation.Totk;

/// <summary>
/// The terrain water: <c>Shader/terrain_water</c>'s program 98, the ordinary water surface, with the <c>TranslucentNear</c> material's
/// <c>gsys_material</c> built against that shading model's own block, and every slice of the three textures the program reads.
/// </summary>
public static class TotkTerrainWater
{
    public const int Program = 98;
    public const string ProgramName = "terrain_water_prog98";
    public const string MaterialName = "TranslucentNear";
    public const string MaterialFile = "terrain_water_material.bin";
    public const string TexturesFile = "terrain_water_textures.json";

    static readonly string[] Textures = ["WaterAlb", "WaterNrm", "WaterEmm"];

    public static bool IsExported(string shadersDir) =>
        File.Exists(Path.Combine(shadersDir, TexturesFile)) && File.Exists(Path.Combine(shadersDir, ProgramName + "_extracted.frag"));

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

    // Every slice of each texture the program reads, mip 0, back to back.
    static void WriteTextures(IRomAccess rom, string shadersDir)
    {
        var written = new List<object>();
        foreach (string name in Textures)
        {
            if (TotkTextures.Find(rom, name) is not { } path)
            {
                Console.WriteLine($"[ExportTerrainWater] texture not found: {name}");
                continue;
            }

            var texture = TotkTextures.LoadAllSlices(rom, path);
            string file = $"terrain_water_{name}_{texture.Width}x{texture.Height}x{texture.ArrayCount}_{texture.Format}.bin";
            using (var stream = File.Create(Path.Combine(shadersDir, file)))
                foreach (var surface in texture.Surfaces)
                    stream.Write(surface.Data);
            written.Add(new { name, file, format = texture.Format.ToString(), width = texture.Width, height = texture.Height, layers = texture.ArrayCount });
            Console.WriteLine($"[ExportTerrainWater] {name}: {texture.Width}x{texture.Height}, {texture.ArrayCount} slice(s), {texture.Format}");
        }
        File.WriteAllText(Path.Combine(shadersDir, TexturesFile), JsonSerializer.Serialize(written));
    }
}
