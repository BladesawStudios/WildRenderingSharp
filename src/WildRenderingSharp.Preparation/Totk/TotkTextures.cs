using BntxSharp;
using ShaderLibrary.CompileTool;
using WildRenderingSharp.Rom;

namespace WildRenderingSharp.Preparation.Totk;

/// <summary>Finds and loads a material texture in TexToGo, whether it ships as a .txtg or as a zstd .bntx.</summary>
public static class TotkTextures
{
    static readonly string[] Extensions = [".txtg", ".bntx.zs", ".bntx"];

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

    public static TxtgTexture Load(IRomAccess rom, string path, int surfaces = int.MaxValue)
    {
        if (path.EndsWith(".txtg", StringComparison.OrdinalIgnoreCase))
            return TxtgTexture.Load(new MemoryStream(rom.ReadAllBytesDirectSpan(path).ToArray()), surfaces);
        return TexToGo.FromBntx(ReadBntx(rom, path), surfaces);
    }

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
