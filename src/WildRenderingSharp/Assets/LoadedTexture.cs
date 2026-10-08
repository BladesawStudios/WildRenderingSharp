using Silk.NET.OpenGL;

namespace WildRenderingSharp.Assets;

public sealed class LoadedTexture
{
    public required uint Handle { get; set; }

    public TextureTarget Target { get; init; } = TextureTarget.Texture2D;
    public required int Width { get; init; }
    public required int Height { get; init; }

    public required string Name { get; init; }
}
