using BntxSharp;
using ShaderLibrary.CompileTool;
using WildRenderingSharp.Rom;

namespace WildRenderingSharp.Preparation.Totk;

/// <summary>
/// A material texture in <c>TexToGo/</c>, whichever container it ships in. Nearly every one is TotK's own <c>.txtg</c>, but a few
/// hundred (among them <c>CmnTex_BakeDefault</c>, which every static world object samples as <c>bake0</c>) are a zstd <c>.bntx</c>.
/// </summary>
public static class TotkTextures
{
    static readonly string[] Extensions = [".txtg", ".bntx.zs", ".bntx"];

    /// <summary>The texture's game-relative path, or null when no layer has it.</summary>
    public static string? Find(IRomAccess rom, string name)
    {
        foreach (string extension in Extensions)
        {
            string path = $"TexToGo/{name}{extension}";
            if (rom.Exists(path))
                return path;
        }
        return null;
    }

    public static TextureHandle? Handle(IRomAccess rom, string name) =>
        Find(rom, name) is { } path ? new TextureHandle(surfaces => Load(rom, path, surfaces)) : null;

    /// <param name="surfaces">How many surfaces, in file order, to read; 0 reads the header only.</param>
    public static TxtgTexture Load(IRomAccess rom, string path, int surfaces = int.MaxValue)
    {
        if (path.EndsWith(".txtg", StringComparison.OrdinalIgnoreCase))
            return TxtgTexture.Load(new MemoryStream(rom.ReadAllBytesDirectSpan(path).ToArray()), surfaces);
        return TexToGo.FromBntx(ReadBntx(rom, path), surfaces);
    }

    /// <summary>Mip 0 of every array slice, in slice order, for the few textures a program reads as an array.</summary>
    public static TxtgTexture LoadAllSlices(IRomAccess rom, string path)
    {
        if (path.EndsWith(".txtg", StringComparison.OrdinalIgnoreCase))
        {
            int slices = Math.Max(1, Load(rom, path, surfaces: 0).ArrayCount);
            var texture = Load(rom, path, slices);
            texture.Surfaces.RemoveAll(s => s.MipLevel != 0);
            return texture;
        }

        BntxTexture source = ReadBntx(rom, path);
        var result = TexToGo.FromBntx(source, surfaces: 0);
        result.ArrayCount = source.ArrayLength;
        for (int slice = 0; slice < source.ArrayLength; slice++)
            result.Surfaces.Add(new TxtgSurface { ArrayLevel = slice, MipLevel = 0, Data = source.GetDeswizzledData(0, slice) });
        return result;
    }

    static BntxTexture ReadBntx(IRomAccess rom, string path) => BntxFile.Load(rom.ReadAllBytesNested(path).ToArray()).Textures[0];
}
