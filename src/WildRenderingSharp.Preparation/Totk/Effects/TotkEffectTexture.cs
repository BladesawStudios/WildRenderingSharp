using ShaderLibrary.CompileTool;
using TexSharp;
using WildRenderingSharp.Rom;

namespace WildRenderingSharp.Preparation.Totk.Effects;

/// <summary>An emitter's texture decoded to plain bytes the renderer can upload: the width and height as two 32-bit integers, then RGBA8.</summary>
internal static class TotkEffectTexture
{
    public static void Export(IRomAccess rom, string name, string path)
    {
        string source = TotkTextures.Find(rom, name) ?? throw new FileNotFoundException($"The effect texture '{name}' is not in TexToGo.");
        var texture = TotkTextures.Load(rom, source, surfaces: 1);
        byte[] data = texture.Surfaces[0].Data;
        byte[] rgba = texture.Format switch
        {
            TxtgFormat.BC1_UNORM or TxtgFormat.BC1_UNORM_SRGB => TextureDecoder.ToRgba8(TextureFormat.Bc1, data, texture.Width, texture.Height),
            TxtgFormat.BC3_UNORM_SRGB => TextureDecoder.ToRgba8(TextureFormat.Bc3, data, texture.Width, texture.Height),
            TxtgFormat.BC4_UNORM => TextureDecoder.ToRgba8(TextureFormat.Bc4, data, texture.Width, texture.Height),
            TxtgFormat.BC5_UNORM => TextureDecoder.ToRgba8(TextureFormat.Bc5, data, texture.Width, texture.Height),
            TxtgFormat.BC7_UNORM => TextureDecoder.ToRgba8(TextureFormat.Bc7, data, texture.Width, texture.Height),
            TxtgFormat.R8G8B8A8_UNORM or TxtgFormat.R8G8B8A8_SRGB => data,
            _ => throw new NotSupportedException($"The effect texture '{name}' is {texture.Format}, which is not decoded."),
        };

        using var file = File.Create(path);
        using var writer = new BinaryWriter(file);
        writer.Write(texture.Width);
        writer.Write(texture.Height);
        writer.Write(rgba);
    }
}
