using Silk.NET.OpenGL;

namespace WildRenderingSharp.Assets;

public sealed class LoadedTexture
{
    public required uint Handle { get; set; }

    /// <summary>What the handle is bound as - a 2D texture, or a host's texture array (<see cref="ExternalTextures"/>).</summary>
    public TextureTarget Target { get; init; } = TextureTarget.Texture2D;
    public required int Width { get; init; }
    public required int Height { get; init; }

    /// <summary>The romfs texture name (e.g. "Cmn_Enemy_DungeonBoss_Eye_Alb"), so a pass can identify a specific asset, as <c>KnownMaterialFixes</c> does.</summary>
    public required string Name { get; init; }
}
