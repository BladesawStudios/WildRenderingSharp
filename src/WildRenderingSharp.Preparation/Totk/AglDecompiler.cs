using System.Text;
using EffectLibraryTest;
using ShaderLibrary;
using CompileToolControlShader = ShaderLibrary.CompileTool.ControlShader;
using ShaderLibrary.Sharc;
using WildRenderingSharp.Rom;

namespace WildRenderingSharp.Preparation.Totk;

/// <summary>Decompiles programs of the engine's agl shader archives to GLSL, one file per macro combination and stage.</summary>
public static class AglDecompiler
{
    public static SharcfbFile Open(IRomAccess rom, string sarcPath, string name)
    {
        string path = $"{sarcPath}//{name}";
        if (!rom.Exists(path))
            throw new FileNotFoundException($"'{sarcPath}' has no \"{name}\" entry - archive layout changed?");
        return new SharcfbFile(new MemoryStream(rom.ReadAllBytesNested(path).ToArray()));
    }

    public static SharcfbFile.ShaderProgram Program(SharcfbFile sharc, string name) =>
        sharc.Programs.FirstOrDefault(p => p.Name == name) ?? throw new InvalidOperationException($"No \"{name}\" program - archive layout changed?");

    public static void Variant(SharcfbFile sharc, SharcfbFile.ShaderProgram program, Dictionary<string, string> combo, string outDir, string name)
    {
        foreach (var (macroName, value) in combo)
        {
            var macro = program.VariationMacros.FirstOrDefault(m => m.Name == macroName)
                ?? throw new InvalidOperationException($"program \"{program.Name}\" declares no macro \"{macroName}\" - archive layout changed?");
            if (!macro.Values.Contains(value))
                throw new InvalidOperationException($"program \"{program.Name}\" macro \"{macroName}\" has no value \"{value}\" - archive layout changed?");
        }

        int written = WriteStages(sharc, program.GetBinaryIndex(program.GetVariationIndex(combo)), outDir, name);
        if (written == 0)
            throw new InvalidOperationException($"program \"{program.Name}\" combination yielded no bytecode - archive layout changed?");
    }

    public static void AllVariants(SharcfbFile sharc, SharcfbFile.ShaderProgram program, string outDir)
    {
        var combos = program.VariationMacros.Count > 0
            ? SharcUtils.GetAllVariationCombinations(program.VariationMacros).ToList()
            : [new Dictionary<string, string>()];

        foreach (var combo in combos)
        {
            int index = combo.Count > 0 ? SharcUtils.GetVariationIndex(program.VariationMacros, combo) : 0;
            string suffix = combo.Count > 0 ? "_" + string.Join("_", combo.Select(kv => $"{kv.Key}{kv.Value}")) : "";
            try
            {
                WriteStages(sharc, program.GetBinaryIndex(index), outDir, $"agl_{program.Name}{suffix}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AglDecompiler] {program.Name}{suffix} failed: {ex.Message}");
            }
        }
    }

    // A program's vertex and pixel stages are the two variations from its binary index.
    static int WriteStages(SharcfbFile sharc, int binaryIndex, string outDir, string name)
    {
        Directory.CreateDirectory(outDir);
        int written = 0;
        for (int stage = 0; stage < 2; stage++)
        {
            int at = binaryIndex + stage;
            if (at < 0 || at >= sharc.Variations.Count || sharc.Variations[at] is not { ByteCode.Length: > 0 } variation)
                continue;

            string kind = variation.Type.ToString().ToLowerInvariant();
            string extension = kind.Contains("pixel") || kind.Contains("frag") ? "frag" : "vert";
            File.WriteAllText(Path.Combine(outDir, $"{name}.{extension}"), ToGlsl(variation));
            written++;
        }
        return written;
    }

    static string ToGlsl(SharcfbFile.ShaderVariation variation)
    {
        string glsl = TegraShaderTranslator.Decompile(variation.ByteCode);
        if (variation.ControlShader is not { Length: > 0 } control)
            return glsl;
        float[] constants = new CompileToolControlShader(control).GetConstantsAsFloats(variation.ByteCode);
        return constants.Length > 0 ? FoldConstants(glsl, constants) : glsl;
    }

    // The immediate constant buffer is baked into the shader's code; leaving its reads dangling references a block nothing binds.
    static string FoldConstants(string code, float[] constants)
    {
        var literals = new Dictionary<string, float>();
        for (int i = 0; i < constants.Length; i++)
        {
            string component = "xyzw"[i % 4].ToString();
            literals[$"vp_c1.data[{i / 4}].{component}"] = constants[i];
            literals[$"fp_c1.data[{i / 4}].{component}"] = constants[i];
            literals[$"vp_c1_1._m0[{i / 4}].{component}"] = constants[i];
        }

        var folded = new StringBuilder();
        foreach (string line in code.Split('\n'))
        {
            string text = line;
            if (text.Contains("_c1.data[") || text.Contains("_c1_1._m0["))
                foreach (var (key, value) in literals.Where(kv => text.Contains(kv.Key)))
                    text = text.Replace(key, value.ToString("R"));
            folded.Append(text).Append('\n');
        }
        return folded.ToString();
    }
}
