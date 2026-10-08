using HarmonyLib;
using McSharp;
using ShaderLibrary.CompileTool;

namespace WildRenderingSharp.Preparation;

/// <summary>
/// Makes ShaderLibrary unpack <c>.bfres.mc</c> files in this process with McSharp. It would otherwise shell out to a Windows-only
/// <c>meshcodec_cli.exe</c> that it looks for beside its own source file.
/// </summary>
static class McSharpDecompression
{
    const string PatchId = "WildRenderingSharp.McSharpDecompression";

    public static void EnsureApplied()
    {
        var target = AccessTools.Method(typeof(TestMaterialDump), nameof(TestMaterialDump.DecompressBfresMc));
        var prefix = new HarmonyMethod(typeof(McSharpDecompression), nameof(Decompress));
        new Harmony(PatchId).Patch(target, prefix);
    }

    static bool Decompress(string mcPath, ref byte[] __result)
    {
        __result = MeshCodec.DecompressMc(File.ReadAllBytes(mcPath), out var status)
            ?? throw new InvalidDataException($"{mcPath}: McSharp could not decode it ({status}).");
        return false;
    }
}
