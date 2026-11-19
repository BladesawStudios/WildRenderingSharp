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

    /// <summary>Prepares the model of an actor or model name, which is a file stem or Stem.Model, into <paramref name="modelFolder"/> and <paramref name="shaderFolder"/>; returns what the export wrote.</summary>
    public static PreparedPackModel Prepare(string romfs, string name, string modelFolder, string shaderFolder, Action<string>? log = null, bool animations = true, IReadOnlyList<string>? animationPacks = null)
    {
        string stem = name.Contains('.') ? name[..name.IndexOf('.')] : name;
        string work = Path.Combine(Path.GetTempPath(), "krs-pack-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            string packPath = Path.Combine(work, "model.bfap");
            string[] stems = [stem, stem + "_Animation", .. animationPacks ?? []];
            AssetOptions options = new(romfs, stems, TextureSelection.Used, Storage: TextureStorage.Linear, AssignTypes: Passes);
            ProgramUse? use = null;
            AssetResult result;
            using (FileStream output = File.Create(packPath)) result = AssetPreparer.Prepare(options, output, u => use = u);
            if (result.Models == 0) throw new FileNotFoundException($"No model matches '{name}' in {romfs}. {string.Join("; ", result.Failures.Take(3).Select(f => f.Reason))}");

            string archives = Path.Combine(work, "shaders");
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

            using AssetPack pack = AssetPack.Open(packPath);
            Dictionary<string, PreparedArchive> opened = [];
            try
            {
                foreach (string file in Directory.EnumerateFiles(archives, "*.bfsp")) opened[Path.GetFileNameWithoutExtension(file)] = PreparedArchive.Open(file);
                string packModel = pack.Of(PackEntryKind.Model).Select(e => e.Name).FirstOrDefault(n => n == stem || n.StartsWith(stem + "/", StringComparison.Ordinal) && (name == stem || n.EndsWith("/" + name[(stem.Length + 1)..], StringComparison.Ordinal)))
                    ?? throw new FileNotFoundException($"The pack has no model for '{name}'");
                using PackModelExporter exporter = new(pack, shader => opened[shader]);
                ExportedModel exported;
                // Models prepared side by side share the program files, so their writes take turns.
                lock (ExportGate) exported = exporter.Export(packModel, name, modelFolder, shaderFolder);
                int anims = 0;
                if (animations)
                {
                    List<string> skipped = [];
                    anims = PackAnimExporter.Export(pack, stems, name, modelFolder, skipped);
                    foreach (string line in skipped.Take(20)) log?.Invoke("animation: " + line);
                }

                return new PreparedPackModel(name, exported.Shapes, exported.Programs, exported.Textures, exported.Problems, anims);
            }
            finally
            {
                foreach (PreparedArchive archive in opened.Values) archive.Dispose();
            }
        }
        finally
        {
            try { Directory.Delete(work, true); } catch (IOException) { }
        }
    }
}

/// <summary>What preparing a model through a pack wrote: its shape, program and texture counts and the problems met.</summary>
sealed record PreparedPackModel(string Name, int Shapes, int Programs, int Textures, IReadOnlyList<string> Problems, int Animations = 0);
