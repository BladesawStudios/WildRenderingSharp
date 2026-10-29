using System.Text.Json;
using BfresLibrary;
using EffectLibraryTest;
using ShaderLibrary;
using ShaderLibrary.CompileTool;
using WildRenderingSharp.Logging;
using WildRenderingSharp.Rom;
using WildRenderingSharp.Storage;

namespace WildRenderingSharp.Preparation.Totk;

/// <summary>Decompiles the G-buffer programs and default material blocks of the terrain and cave shading models from their shader archives.</summary>
public static class TotkShaderExports
{
    public sealed record ShadingModelExport(string Archive, string Model, int[] Programs, string ProgramPrefix, string MaterialFile)
    {
        public string ProgramName(int program) => $"{ProgramPrefix}{program}";
    }

    public static readonly ShadingModelExport Terrain = new("terrain", "terrain", [2, 32, 62], "terrain_prog", "terrain_gsys_material.bin");

    // The Depths' ground, the quad mesh, whose archive holds one shading model for each level of detail besides the generic one. Programs 1 and 2 are its depth
    // and G-buffer programs, as they are the chunked cave's.
    public static readonly ShadingModelExport Depths = new("cave_quadmesh", "cave_quad_mesh_generic", [1, 2], "depths_prog", "depths_gsys_material.bin");

    public static readonly ShadingModelExport Cave = new("cave", "cave_chunked_mesh_generic", [1, 2], "cave_prog", "cave_gsys_material.bin");

    public static bool IsExported(ShadingModelExport export, string shadersDir) =>
        File.Exists(Path.Combine(shadersDir, export.MaterialFile))
        && export.Programs.All(p => File.Exists(Path.Combine(shadersDir, export.ProgramName(p) + "_extracted.frag"))
            && File.Exists(Path.Combine(shadersDir, export.ProgramName(p) + "_extracted.vert")));

    public static void Export(IRomAccess rom, ShadingModelExport export, string shadersDir)
    {
        Directory.CreateDirectory(shadersDir);
        var models = OpenArchive(rom, export.Archive).ShaderModels;
        var shadingModel = models.TryGetValue(export.Model, out var found)
            ? found
            : throw new InvalidOperationException($"'{export.Archive}' has no shader model \"{export.Model}\"; it holds: {string.Join(", ", models.Keys)}");
        foreach (int program in export.Programs)
            WriteProgram(shadingModel, program, export.ProgramName(program), shadersDir);

        var block = shadingModel.UniformBlocks["gsys_material"];
        byte[] defaults = block.DefaultBuffer ?? new byte[block.Size];
        File.WriteAllBytes(Path.Combine(shadersDir, export.MaterialFile), defaults);
        Log.Info($"[TotkShaderExports] {export.Archive} programs {string.Join(", ", export.Programs)} and gsys_material defaults ({defaults.Length} bytes)");
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
            AtomicFile.WriteAllText(target, ShaderExtract.GetCode(code, reflection));
        }
    }
}
