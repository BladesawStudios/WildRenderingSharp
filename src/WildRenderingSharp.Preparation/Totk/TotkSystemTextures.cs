using BntxSharp;
using ShaderLibrary.CompileTool;
using TexSharp;
using WildRenderingSharp.Logging;
using WildRenderingSharp.Rom;

namespace WildRenderingSharp.Preparation.Totk;

/// <summary>Decodes the shared textures shaders sample but no material owns (the 3D noise volume, the sun and moon sprites) into plain bytes for the renderer.</summary>
public static class TotkSystemTextures
{
    const string NoiseAsset = "3DWorleyPerlinNoise_Fi";

    public static void ExtractProc3DNoise(IRomAccess rom, string outDir)
    {
        Directory.CreateDirectory(outDir);
        string path = rom.Exists($"TexToGo/{NoiseAsset}.bntx") ? $"TexToGo/{NoiseAsset}.bntx" : $"TexToGo/{NoiseAsset}.bntx.zs";
        var texture = BntxFile.Load(rom.ReadAllBytesNested(path).ToArray()).Textures[0];
        int width = texture.Width, height = texture.Height, depth = texture.Depth;
        byte[] blocks = texture.GetDeswizzledData();

        int sliceBytes = blocks.Length / depth;
        var decoded = new byte[width * height * depth];
        for (int z = 0; z < depth; z++)
            RedChannel(TextureFormat.Bc4, blocks.AsSpan(z * sliceBytes, sliceBytes), width, height, decoded.AsSpan(z * width * height));

        Write(outDir, "Proc3DNoise", ".r8", decoded, $"{width} {height} {depth}");
        Log.Info($"[SystemTextures] cTex_Proc3DNoise <- {NoiseAsset}: {width}x{height}x{depth}, {decoded.Length} bytes -> {outDir}");
    }

    public static void ExtractSkyBodies(IRomAccess rom, string outDir)
    {
        Directory.CreateDirectory(outDir);
        Try(() => ExtractMask(rom, outDir, "Etc_Sun_A_Alb", "SunDisc"), "SunDisc");
        for (int phase = 1; phase <= 8; phase++)
        {
            int p = phase;
            Try(() => ExtractRg(rom, outDir, $"Etc_Moon_A_Alb.{p}", $"Moon{p}"), $"Moon{p}");
        }
    }

    static void ExtractMask(IRomAccess rom, string outDir, string textureName, string outName)
    {
        if (LoadFirstSurface(rom, textureName, TxtgFormat.BC4_UNORM) is not { } texture)
            return;

        var decoded = new byte[texture.Width * texture.Height];
        RedChannel(TextureFormat.Bc4, texture.Surfaces[0].Data, texture.Width, texture.Height, decoded);
        Write(outDir, outName, ".r8", decoded, $"{texture.Width} {texture.Height} 1");
        Log.Info($"[SystemTextures] {outName} <- {textureName}: {texture.Width}x{texture.Height}, {decoded.Length} bytes -> {outDir}");
    }

    static void ExtractRg(IRomAccess rom, string outDir, string textureName, string outName)
    {
        if (LoadFirstSurface(rom, textureName, TxtgFormat.BC5_UNORM) is not { } texture)
            return;

        byte[] rgba = TextureDecoder.ToRgba8(TextureFormat.Bc5, texture.Surfaces[0].Data, texture.Width, texture.Height);
        var decoded = new byte[texture.Width * texture.Height * 2];
        for (int i = 0; i < texture.Width * texture.Height; i++)
        {
            decoded[i * 2] = rgba[i * 4];
            decoded[i * 2 + 1] = rgba[i * 4 + 1];
        }
        Write(outDir, outName, ".rg8", decoded, $"{texture.Width} {texture.Height} 2");
        Log.Info($"[SystemTextures] {outName} <- {textureName}: {texture.Width}x{texture.Height} BC5, {decoded.Length} bytes -> {outDir}");
    }

    static TxtgTexture? LoadFirstSurface(IRomAccess rom, string textureName, TxtgFormat expected)
    {
        string path = $"TexToGo/{textureName}.txtg";
        if (!rom.Exists(path))
        {
            Log.Warning($"[SystemTextures] '{path}' not found.");
            return null;
        }

        var texture = TotkTextures.Load(rom, path, surfaces: 1);
        if (texture.Format == expected)
            return texture;
        Log.Info($"[SystemTextures] '{textureName}' is {texture.Format}, expected {expected} - skipping.");
        return null;
    }

    internal static void RedChannel(TextureFormat format, ReadOnlySpan<byte> blocks, int width, int height, Span<byte> destination)
    {
        byte[] rgba = TextureDecoder.ToRgba8(format, blocks.ToArray(), width, height);
        for (int i = 0; i < width * height; i++)
            destination[i] = rgba[i * 4];
    }

    static void Write(string outDir, string name, string extension, byte[] texels, string dims)
    {
        File.WriteAllBytes(Path.Combine(outDir, name + extension), texels);
        File.WriteAllText(Path.Combine(outDir, name + ".dims.txt"), dims);
    }

    static void Try(Action work, string what)
    {
        try { work(); }
        catch (Exception ex) { Log.Warning($"[SystemTextures] {what} unavailable: {ex.Message}"); }
    }
}
