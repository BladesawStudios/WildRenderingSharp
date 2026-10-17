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

    // A texture's header (size, format, channel select) is asked for by every model that uses it, and thousands share a few thousand textures, so it is read
    // once per ROM. Only the header is kept: a request for pixel data is read from the ROM as before.
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IRomAccess, System.Collections.Concurrent.ConcurrentDictionary<string, TxtgTexture>> Headers = new();

    public static TextureHandle? Handle(IRomAccess rom, string name)
    {
        if (Find(rom, name) is not { } path)
            return null;
        var headers = Headers.GetOrCreateValue(rom);
        return new TextureHandle(surfaces => surfaces == 0 ? headers.GetOrAdd(path, p => Load(rom, p, 0)) : Load(rom, path, surfaces));
    }

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
