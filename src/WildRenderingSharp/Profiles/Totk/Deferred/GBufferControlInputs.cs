using Silk.NET.OpenGL;
using WildRenderingSharp.Gpu;

namespace WildRenderingSharp.Profiles.Totk.Deferred;

/// <summary>
/// The system textures a few G-buffer programs read as a control value rather than as an image: the dungeon entrance's depth program picks its mode
/// from the texel at the centre of <c>cTex_GBuffAlbedo</c>, and its masks are read from the others.
/// </summary>
/// <remarks>
/// The mode is the texel's three channels, each zero or one. The one used is the plain one, which writes the surface's own depth and nothing else; left
/// unbound, the texel is zero, which is the entrance's cut mode and writes a depth of zero.
/// </remarks>
internal sealed class GBufferControlInputs : IDisposable
{
    const int AlbedoUnit = 0, MaterialIdUnit = 2, DepthUnit = 6, DungeonEntranceMaskUnit = 24;

    readonly GL _gl;
    readonly uint _plainMode, _empty;

    public GBufferControlInputs(GL gl)
    {
        _gl = gl;
        _plainMode = ConstantTextures.Rgba(gl, 1f, 0f, 0f, 0f);
        _empty = ConstantTextures.Rgba(gl, 0f, 0f, 0f, 0f);
    }

    public void Bind()
    {
        _gl.BindTextureAt(AlbedoUnit, _plainMode);
        foreach (int unit in new[] { MaterialIdUnit, DepthUnit, DungeonEntranceMaskUnit })
            _gl.BindTextureAt(unit, _empty);
    }

    public void Dispose()
    {
        _gl.DeleteTexture(_plainMode);
        _gl.DeleteTexture(_empty);
    }
}
