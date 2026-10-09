using System.Security.Cryptography;
using ShaderLibrary.CompileTool;
using WildRenderingSharp.Rom;

namespace WildRenderingSharp.Preparation.Totk;

/// <summary>
/// Points ShaderLibrary's shared string table, which names the RenderInfo, ShaderParam and Option keys of TotK's V10 materials,
/// at the copy the ROM resolves, so a mod's extended table wins like any other file. The table reads one fixed path under a root
/// folder, so the winning copy is written to a folder named for its hash.
/// </summary>
public static class TotkStringTable
{
    const string TablePath = "Shader/ExternalBinaryString.bfres.mc";

    public static void Select(IRomAccess rom, CacheLayout cache)
    {
        ReadOnlySpan<byte> table = rom.ReadAllBytesDirectSpan(TablePath);
        string folder = Path.Combine(cache.Root, "_totk_strings", Convert.ToHexString(SHA256.HashData(table))[..16]);
        string file = Path.Combine(folder, "Shader", "ExternalBinaryString.bfres.mc");
        if (!File.Exists(file))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            string temp = $"{file}.{Guid.NewGuid():N}.tmp";
            File.WriteAllBytes(temp, table.ToArray());
            File.Move(temp, file, overwrite: true);
        }
        ExternalBinaryStringTable.RomfsRoot = folder;
    }
}
