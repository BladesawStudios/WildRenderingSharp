using Silk.NET.OpenGL;
using WildRenderingSharp.Assets.Textures;

namespace WildRenderingSharp.Pipeline.Drawing;

/// <summary>
/// The baked lighting every static object samples when it has none of its own (<c>CmnTex_BakeDefault</c>), at a strength the viewer sets: the game's
/// is full, which makes its G-buffer programs write the largest outline radius and the full toon amount.
/// </summary>
internal sealed unsafe class DefaultBake(GL gl) : IDisposable
{
    const string DefaultName = "CmnTex_BakeDefault", BakeSampler = "bake0";
    const float FullStrength = 1f;

    LoadedTexture? _texture;
    float _written = float.NaN;

    // The baked lighting's value, 0 to 1, for every object without a bake of its own.
    public float Strength { get; set; } = FullStrength;

    // The texture to bind where a shape's sampler holds the default bake, or the sampler's own texture otherwise.
    public LoadedTexture Substitute(string samplerKey, LoadedTexture own) =>
        Strength < FullStrength && samplerKey == BakeSampler && own.Name == DefaultName ? Current() : own;

    public void Dispose()
    {
        if (_texture is not null)
            gl.DeleteTexture(_texture.Handle);
    }

    // One texel of the strength, the red channel the bake programs read, rewritten only when the strength changes.
    LoadedTexture Current()
    {
        if (_texture is null)
        {
            uint handle = gl.GenTexture();
            gl.BindTexture(TextureTarget.Texture2D, handle);
            gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.R8, 1, 1, 0, PixelFormat.Red, PixelType.UnsignedByte, null);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            _texture = new LoadedTexture { Handle = handle, Width = 1, Height = 1, Name = "(default bake)" };
        }

        if (_written != Strength)
        {
            byte texel = (byte)MathF.Round(Math.Clamp(Strength, 0f, 1f) * 255f);
            gl.BindTexture(TextureTarget.Texture2D, _texture.Handle);
            gl.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, 1, 1, PixelFormat.Red, PixelType.UnsignedByte, &texel);
            gl.BindTexture(TextureTarget.Texture2D, 0);
            _written = Strength;
        }
        return _texture;
    }
}
