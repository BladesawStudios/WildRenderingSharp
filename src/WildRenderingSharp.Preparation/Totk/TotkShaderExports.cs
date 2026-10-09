using System.Text.Json;
using BfresLibrary;
using EffectLibraryTest;
using ShaderLibrary;
using ShaderLibrary.CompileTool;
using WildRenderingSharp.Rom;

namespace WildRenderingSharp.Preparation.Totk;

/// <summary>
/// The G-buffer programs and default <c>gsys_material</c> blocks of the shading models the terrain and caves draw with, decompiled out of
/// their shader archives. The game's own vertex stages read the page files as stored, so no host vertex stage is needed.
/// </summary>
public static class TotkShaderExports
{
    /// <param name="Programs">Terrain's G-buffer programs are 2 (regular), 32 and 62 (the coarser levels); cave's are 2 (G-buffer) and 1 (depth only).</param>
    public sealed record ShadingModelExport(string Archive, string Model, int[] Programs, string ProgramPrefix, string MaterialFile)
    {
        public string ProgramName(int program) => $"{ProgramPrefix}{program}";
    }

    public static readonly ShadingModelExport Terrain = new("terrain", "terrain", [2, 32, 62], "terrain_prog", "terrain_gsys_material.bin");

    public static readonly ShadingModelExport Cave = new("cave", "cave_chunked_mesh_generic", [1, 2], "cave_prog", "cave_gsys_material.bin");

    public static bool IsExported(ShadingModelExport export, string shadersDir) =>
        File.Exists(Path.Combine(shadersDir, export.MaterialFile))
        && export.Programs.All(p => File.Exists(Path.Combine(shadersDir, export.ProgramName(p) + "_extracted.frag"))
            && File.Exists(Path.Combine(shadersDir, export.ProgramName(p) + "_extracted.vert")));

    public static void Export(IRomAccess rom, ShadingModelExport export, string shadersDir)
    {
        Directory.CreateDirectory(shadersDir);
        var shadingModel = OpenArchive(rom, export.Archive).ShaderModels[export.Model];
        foreach (int program in export.Programs)
            WriteProgram(shadingModel, program, export.ProgramName(program), shadersDir);

        var block = shadingModel.UniformBlocks["gsys_material"];
        byte[] defaults = block.DefaultBuffer ?? new byte[block.Size];
        File.WriteAllBytes(Path.Combine(shadersDir, export.MaterialFile), defaults);
        Console.WriteLine($"[TotkShaderExports] {export.Archive} programs {string.Join(", ", export.Programs)} and gsys_material defaults ({defaults.Length} bytes)");
    }

    static BfshaFile OpenArchive(IRomAccess rom, string archive)
    {
        string path = rom.Enumerate("Shader", $"{archive}.*bfsha*").First();
        return new BfshaFile(new MemoryStream(rom.ReadAllBytesNested(path).ToArray()));
    }

    internal static void WriteProgram(ShaderModel shadingModel, int program, string name, string shadersDir)
    {
        var binary = shadingModel.GetVariation(program).BinaryProgram;
        foreach (var (code, reflection, extension) in new[] { (binary.VertexShader, binary.VertexShaderReflection, "vert"), (binary.FragmentShader, binary.FragmentShaderReflection, "frag") })
        {
            if (code?.ByteCode == null)
                continue;
            string target = Path.Combine(shadersDir, $"{name}_extracted.{extension}");
            File.WriteAllText(target + ".tmp", ShaderExtract.GetCode(code, reflection));
            File.Move(target + ".tmp", target, overwrite: true);
        }
    }
}
