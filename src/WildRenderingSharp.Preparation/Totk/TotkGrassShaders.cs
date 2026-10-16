using System.Security.Cryptography;
using System.Text.Json;
using BfresLibrary;
using EffectLibraryTest;
using ShaderLibrary;
using WildRenderingSharp.Logging;
using WildRenderingSharp.Rom;
using WildRenderingSharp.Storage;

namespace WildRenderingSharp.Preparation.Totk;

/// <summary>Decompiles the G-buffer program of each of the grass kinds from the game's grass shader archive.</summary>
/// <remarks>
/// The archive holds three programs for each kind of vegetation, the last of which is the one that fills the G-buffer. For each kind
/// and stage <see cref="Export"/> writes a JSON file holding the decompiled GLSL as the translator wrote it, the constants the
/// program's control shader bakes in, and the names of its uniform blocks (by slot) and samplers (by the translator's hex name), which
/// is everything a host needs to spell the program for its own context. Nothing the program says is changed. The files go in a folder
/// named by a hash of the archive, so a different game version is exported again.
/// </remarks>
public static class TotkGrassShaders
{
    const string Archive = "grass";
    const string Model = "grass";
    const string RenderTypeOption = "grass_render_type";

    /// <summary>The grass kinds by name, with the archive's render type each is the program of.</summary>
    public static readonly (string Kind, string Type)[] Kinds =
    [
        ("Blade", "grass_blade"), ("Cross", "grass_cross"), ("Cover", "grass_cover"),
        ("Turf", "turf_blade"), ("TurfCross", "turf_cross"),
        ("FlowerPetal", "flower_petal"), ("FlowerNormal", "flower_normal"), ("FlowerLong", "flower_long"),
        ("Leaves", "fallen_leaves"), ("Shell", "fallen_shell"), ("Chips", "fallen_chips"), ("Stone", "fallen_stone"),
    ];

    static readonly string[] Stages = ["Vertex", "Fragment"];

    /// <summary>The file a kind's stage is exported to, inside the export folder.</summary>
    public static string FileName(string kind, string stage) => $"{kind}{stage}.json";

    /// <summary>The export folder for the archive in the rom, and whether it is complete.</summary>
    public static (string Folder, bool Complete) Locate(IRomAccess rom, CacheLayout cache, out byte[] archive)
    {
        archive = ReadArchive(rom);
        string folder = Path.Combine(cache.GrassShaders, Convert.ToHexString(SHA256.HashData(archive))[..16].ToLowerInvariant());
        return (folder, Kinds.All(k => Stages.All(s => File.Exists(Path.Combine(folder, FileName(k.Kind, s))))));
    }

    /// <summary>Exports every kind's programs if the folder is not already complete, and returns the folder.</summary>
    public static string Export(IRomAccess rom, CacheLayout cache, bool force)
    {
        var (folder, complete) = Locate(rom, cache, out byte[] archive);
        if (complete && !force)
            return folder;

        ShaderModel model = new BfshaFile(new MemoryStream(archive)).ShaderModels.Values.FirstOrDefault(m => m.Name == Model)
            ?? throw new InvalidOperationException($"The grass archive has no \"{Model}\" shader model - archive layout changed?");
        Directory.CreateDirectory(folder);
        foreach (var (kind, type) in Kinds)
            ExportKind(model, kind, type, folder);
        Log.Info($"[TotkGrassShaders] {Kinds.Length} kinds -> {folder}");
        return folder;
    }

    static byte[] ReadArchive(IRomAccess rom)
    {
        string path = rom.Enumerate("Shader", $"{Archive}.*bfsha*").FirstOrDefault()
            ?? throw new FileNotFoundException("Shader/grass.*.bfsha is not in the romfs.");
        return rom.ReadAllBytesNested(path).ToArray();
    }

    // The last of a type's programs is the G-buffer pass.
    static void ExportKind(ShaderModel model, string kind, string type, string folder)
    {
        int program = Enumerable.Range(0, model.Programs.Count).LastOrDefault(i => model.GetOptionChoice(i, RenderTypeOption) == "grass_render_" + type, -1);
        if (program < 0)
            throw new InvalidOperationException($"The grass archive has no program for \"{type}\" - archive layout changed?");

        var binary = model.GetVariation(program).BinaryProgram;
        WriteStage(binary.VertexShader, binary.VertexShaderReflection, Path.Combine(folder, FileName(kind, Stages[0])));
        WriteStage(binary.FragmentShader, binary.FragmentShaderReflection, Path.Combine(folder, FileName(kind, Stages[1])));
    }

    static void WriteStage(BnshFile.ShaderCode code, BnshFile.ShaderReflectionData reflection, string target)
    {
        float[] constants = new global::ShaderLibrary.ControlShader(code.ControlCode).GetConstantsAsFloats(code.ByteCode);
        var stage = new
        {
            glsl = TegraShaderTranslator.Decompile(code.ByteCode),
            constants,
            blocks = BlockSlots(reflection),
            samplers = SamplerNames(reflection),
        };
        AtomicFile.WriteAllText(target, JsonSerializer.Serialize(stage));
    }

    // The program's uniform blocks by name, with the slot each is bound to.
    static Dictionary<string, int> BlockSlots(BnshFile.ShaderReflectionData reflection)
    {
        var blocks = new Dictionary<string, int>();
        foreach (string name in reflection.UniformBuffers.Keys)
            if (reflection.GetConstantBufferLocation(name) is >= 0 and int slot)
                blocks[name] = slot;
        return blocks;
    }

    // The program's samplers by the name the translator gives the slot each is bound to.
    static Dictionary<string, string> SamplerNames(BnshFile.ShaderReflectionData reflection)
    {
        var samplers = new Dictionary<string, string>();
        foreach (string name in reflection.Samplers.Keys)
            if (reflection.GetSamplerLocation(name) is >= 0 and int slot)
                samplers[$"{2 * slot + 8:X}"] = name;
        return samplers;
    }
}
