using System.Security.Cryptography;
using System.Text;
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

    // Bumped when the prepared archive's content changes, so older shared files are not reused.
    const string SharedFormat = "1";

    // The cache folder, which holds _packs: what is shared between models lives beside it.
    static string CacheRoot(string shaderFolder) => Path.GetDirectoryName(Path.GetDirectoryName(shaderFolder)!)!;

    // The archives read last stay in memory while the process lives (the snapshot server prepares model after model), so a model that needs a program prepared fresh does not read and parse the
    // 90 MB archive again. Only the few archives of the models' own shaders are kept, each tied to the file's size and time.
    static readonly Dictionary<string, (long Length, long Ticks, BfshaFile File)> Archives = new(StringComparer.OrdinalIgnoreCase);

    // The file on disk an archive's path leads to: the archive itself, or for one inside a pack (Breath of the Wild's Bootup_Graphics.pack\Shader\...) the pack file that holds it.
    // The path is taken from the romfs when it is not rooted. Null when no file is found, so the archive is read each time as before.
    static FileInfo? RealFile(string romfs, string path)
    {
        string current = Path.IsPathRooted(path) ? path : Path.Combine(romfs, path);
        while (!File.Exists(current) && Path.GetDirectoryName(current) is { Length: > 0 } parent) current = parent;
        return File.Exists(current) ? new FileInfo(current) : null;
    }

    static BfshaFile ReadArchive(RomfsFiles files, string romfs, string path)
    {
        FileInfo? info = RealFile(romfs, path);
        if (info is null) return BfshaFile.Read(files.Read(path));
        lock (Archives)
        {
            if (Archives.TryGetValue(path, out var known) && known.Length == info.Length && known.Ticks == info.LastWriteTimeUtc.Ticks) return known.File;
        }

        BfshaFile archive = BfshaFile.Read(files.Read(path));
        lock (Archives)
        {
            if (Archives.Count >= 3) Archives.Clear();
            Archives[path] = (info.Length, info.LastWriteTimeUtc.Ticks, archive);
        }

        return archive;
    }

    // Where the archive prepared for these programs is kept for every model: beside the models' packs, named by the archive file (its name, size and time) and the programs asked of it.
    static string? SharedProgramFile(string romfs, string shaderFolder, string archivePath, string archiveName, IReadOnlyDictionary<string, IReadOnlySet<int>> wanted)
    {
        try
        {
            FileInfo info = RealFile(romfs, archivePath) ?? throw new FileNotFoundException(archivePath);
            StringBuilder key = new();
            key.Append(SharedFormat).Append('|').Append(Path.GetFileName(archivePath)).Append('|').Append(info.Name).Append('|').Append(info.Length).Append('|').Append(info.LastWriteTimeUtc.Ticks).Append('|').Append(archiveName);
            foreach ((string model, IReadOnlySet<int> programs) in wanted.OrderBy(w => w.Key, StringComparer.Ordinal))
                key.Append('|').Append(model).Append(':').Append(string.Join(',', programs.Order()));
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key.ToString())))[..24];
            return Path.Combine(CacheRoot(shaderFolder), "_programs", $"{archiveName}-{hash}.bfsp");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    // Copied in under a temporary name and moved into place, so a file another model sees is always whole.
    static void KeepShared(string prepared, string shared)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(shared)!);
            string temporary = shared + "." + Environment.ProcessId + ".tmp";
            File.Copy(prepared, temporary, true);
            File.Move(temporary, shared, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
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
                // An archive is named by the start of its file name (material.Product.110...bfsha.zs is material). When the programs wanted of that name were prepared before, from this same file, the prepared
                // archive is copied from the shared folder and the 90 MB archive is never read; otherwise it is read and prepared as before, and kept there for the next model that wants the same programs.
                string guess = Path.GetFileName(path).Split('.')[0];
                string? shared = use?.Of(guess) is { Count: > 0 } known ? SharedProgramFile(romfs, shaderFolder, path, guess, known) : null;
                if (shared is not null && File.Exists(shared))
                {
                    File.Copy(shared, Path.Combine(archives, guess + ".bfsp"), true);
                    log?.Invoke($"prepared shaders of {guess}");
                    continue;
                }

                System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
                BfshaFile archive = ReadArchive(files, romfs, path);
                long read = clock.ElapsedMilliseconds;
                IReadOnlyDictionary<string, IReadOnlySet<int>>? wanted = use?.Of(archive.Name);
                if (wanted is null || wanted.Count == 0) continue;
                string target = Path.Combine(archives, archive.Name + ".bfsp");
                using (FileStream output = File.Create(target))
                    ArchivePreparer.Prepare(archive, output, 5, 0, null, wanted, StageText.Reflected, Path.Combine(CacheRoot(shaderFolder), "_stages"));
                if (archive.Name == guess && shared is not null) KeepShared(target, shared);
                log?.Invoke($"prepared shaders of {archive.Name} (read {read} ms, prepare {clock.ElapsedMilliseconds - read} ms)");
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
