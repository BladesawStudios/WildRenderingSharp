using EffectLibraryTest;
using ShaderLibrary;

namespace WildRenderingSharp.Preparation.Totk.Effects;

/// <summary>Decompiles one program of an effect file's shader archive, the vertex and fragment stages of an emitter's shader index.</summary>
internal static class TotkEffectProgram
{
    public static void Export(byte[] archive, int index, string name, string shadersDir)
    {
        var shaders = new BnshFile(new MemoryStream(archive));
        if ((uint)index >= (uint)shaders.Variations.Count)
            throw new InvalidDataException($"The shader archive has {shaders.Variations.Count} programs, not {index + 1}.");

        var program = shaders.Variations[index].BinaryProgram;
        Directory.CreateDirectory(shadersDir);
        Write(Path.Combine(shadersDir, name + ".vert"), program.VertexShader, program.VertexShaderReflection);
        // The fragment stage goes last: it is what marks the program as exported.
        Write(Path.Combine(shadersDir, name + ".frag"), program.FragmentShader, program.FragmentShaderReflection);
    }

    static void Write(string path, BnshFile.ShaderCode? code, BnshFile.ShaderReflectionData? reflection)
    {
        if (code?.ByteCode is null)
            throw new InvalidDataException($"The program has no code for {Path.GetFileName(path)}.");
        File.WriteAllText(path, ShaderExtract.GetCode(code, reflection));
    }
}
