using BfShaderSharp.Assets;
using BfShaderSharp.Bfsha;
using BfShaderSharp.Pack;
using BfShaderSharp.Pack.Records;
using BfShaderSharp.Prepared;
using BfShaderSharp.Preparation;

using KingRenderingSharp.Content.Packs;

namespace KingRenderingSharp.Packing;

/// <summary>Prepares one model of a game's romfs through BfShaderSharp and writes it into a cache folder in the layout the loader reads.</summary>
/// <remarks>
/// The model's files are read into an asset pack with linear textures and the programs of its three draw passes, the shader archives it uses are prepared with the
/// programs' GLSL written under their reflection names, and the pack is exported. The pack and the prepared archives are temporary.
/// </remarks>
static class PackPreparer
{
    static readonly object ExportGate = new();
    static readonly string[] Passes = ["gsys_assign_gbuffer", "gsys_assign_zonly", "gsys_assign_material"];

    // A host names a model file for the file it came from and the hash of its bytes (Animal_Bass-3BFC81E3A401); the stem of the file is what the game's Tex files are named for.
    static string OriginalStem(string name)
    {
        int dash = name.LastIndexOf('-');
        return dash > 0 && name.Length - dash - 1 == 12 && name[(dash + 1)..].All(Uri.IsHexDigit) ? name[..dash] : name;
    }

    /// <summary>Prepares the model of an actor or model name, which is a file stem or Stem.Model: its pack is written to <paramref name="packPath"/> and the prepared archives of the shaders it uses to <paramref name="shaderFolder"/>.</summary>
    public static PreparedPackModel Prepare(string romfs, string name, string packPath, string shaderFolder, Action<string>? log = null, bool animations = true, IReadOnlyList<string>? animationPacks = null, string? modelFile = null, IReadOnlyList<string>? layers = null)
    {
        string stem = name.Contains('.') ? name[..name.IndexOf('.')] : name;
        Directory.CreateDirectory(Path.GetDirectoryName(packPath)!);
        string temporary = packPath + ".tmp";
        string[] stems = animations ? [stem, stem + "_Animation", .. animationPacks ?? []] : [stem];
        AssetOptions options = new(romfs, stems, TextureSelection.Used, Storage: TextureStorage.Linear, AssignTypes: Passes, Layers: layers);
        ProgramUse? use = null;
        AssetResult result;
        // A model file is prepared as it is (an edited model, say) and the pack holds only it; otherwise the model is found in the romfs.
        using (FileStream output = File.Create(temporary))
            result = modelFile is null ? AssetPreparer.Prepare(options, output, u => use = u) : ModelAssetPreparer.Prepare(options, stem, File.ReadAllBytes(modelFile), [], output, u => use = u, OriginalStem(stem));
        if (result.Models == 0)
        {
            File.Delete(temporary);
            throw new FileNotFoundException($"No model matches '{name}' in {romfs}. {string.Join("; ", result.Failures.Take(3).Select(f => f.Reason))}");
        }

        string archives = shaderFolder + ".tmp";
        if (Directory.Exists(archives)) Directory.Delete(archives, true);
        Directory.CreateDirectory(archives);
        using (RomfsFiles files = new(romfs))
        {
            foreach (string path in files.ShaderFiles())
            {
                BfshaFile archive = BfshaFile.Read(files.Read(path));
                IReadOnlyDictionary<string, IReadOnlySet<int>>? wanted = use?.Of(archive.Name);
                if (wanted is null || wanted.Count == 0) continue;
                using FileStream output = File.Create(Path.Combine(archives, archive.Name + ".bfsp"));
                ArchivePreparer.Prepare(archive, output, 5, 0, null, wanted, StageText.Reflected);
                log?.Invoke($"prepared shaders of {archive.Name}");
            }
        }

        // The pack and its archives are moved into place together, so a model is prepared only once both are there.
        if (Directory.Exists(shaderFolder)) Directory.Delete(shaderFolder, true);
        Directory.Move(archives, shaderFolder);
        File.Move(temporary, packPath, true);

        ModelPack opened = new(packPath, shaderFolder, name);
        try
        {
            using AssetPack pack = AssetPack.Open(packPath);
            string packModel = ModelPack.FindModel(pack, name) ?? throw new FileNotFoundException($"The pack has no model for '{name}'");
            ModelRecord record = pack.Model(packModel)!;
            int programs = Directory.EnumerateFiles(shaderFolder, "*.bfsp").Count();
            int textures = pack.Of(PackEntryKind.Texture).Count();
            int anims = pack.Of(PackEntryKind.SkeletalAnim).Count() + pack.Of(PackEntryKind.MaterialAnim).Count() + pack.Of(PackEntryKind.VisibilityAnim).Count();
            List<string> problems = [.. ((PackModelSource)opened.Source).Problems];
            return new PreparedPackModel(name, record.Shapes.Count, programs, textures, problems, anims);
        }
        finally
        {
            opened.Dispose();
        }
    }
}

/// <summary>What preparing a model through a pack wrote: its shape, program and texture counts and the problems met.</summary>
sealed record PreparedPackModel(string Name, int Shapes, int Programs, int Textures, IReadOnlyList<string> Problems, int Animations = 0);
