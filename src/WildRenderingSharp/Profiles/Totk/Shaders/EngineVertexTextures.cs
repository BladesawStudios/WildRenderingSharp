using Silk.NET.OpenGL;

namespace WildRenderingSharp.Profiles.Totk.Shaders;

/// <summary>
/// Neutral stand-ins for the vertex textures the game's engine renders itself: no wind swell, grass pressed nowhere (the lie map is
/// read as <c>2x - 1</c>, so 0.5 is no push), no thickness.
/// </summary>
public sealed class EngineVertexTextures(GL gl) : IDisposable
{
    uint _windSwell, _lieMap, _thickness;

    public void Bind()
    {
        if (_windSwell == 0)
        {
            _windSwell = Constant(0f, 0f);
            _lieMap = Constant(0.5f, 0.5f);
            _thickness = Constant(0f, 0f);
        }
        BindUnit(TotkGlsl.WindSwellUnit, _windSwell);
        BindUnit(TotkGlsl.LieMapUnit, _lieMap);
        BindUnit(TotkGlsl.ThicknessUnit, _thickness);
        gl.ActiveTexture(TextureUnit.Texture0);
    }

    void BindUnit(int unit, uint handle)
    {
        gl.ActiveTexture(TextureUnit.Texture0 + unit);
        gl.BindTexture(TextureTarget.Texture2D, handle);
    }

    unsafe uint Constant(float r, float g)
    {
        uint handle = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, handle);
        float* texel = stackalloc float[] { r, g, 0f, 1f };
        gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba16f, 1, 1, 0, PixelFormat.Rgba, PixelType.Float, texel);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
        gl.BindTexture(TextureTarget.Texture2D, 0);
        return handle;
    }

    public void Dispose()
    {
        foreach (uint texture in new[] { _windSwell, _lieMap, _thickness })
            if (texture != 0)
                gl.DeleteTexture(texture);
        _windSwell = _lieMap = _thickness = 0;
    }
}
