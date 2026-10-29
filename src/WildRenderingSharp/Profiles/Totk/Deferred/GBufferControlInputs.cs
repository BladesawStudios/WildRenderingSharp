using Silk.NET.OpenGL;
using WildRenderingSharp.Gpu;

namespace WildRenderingSharp.Profiles.Totk.Deferred;

/// <summary>
/// The system textures a few G-buffer programs read as a control value rather than as an image: the dungeon entrance's depth program picks its mode
/// from the texel at the centre of <c>cTex_GBuffAlbedo</c>, and its masks are read from the others. The G-buffer pass and the shadow pass both bind them, since
/// both draw that program.
/// </summary>
/// <remarks>
/// The mode is the texel's three channels, each zero or one. The plain one, (1, 0, 0), writes the surface's own depth and cuts out what its material does; left
/// unbound, the texel is zero, which is the entrance's cut mode and writes a depth of zero. (0, 0, 1) cuts out nothing, writes the surface's own depth and puts
/// that depth in its colour as well, which <see cref="EntranceInterior"/> draws the whole hull with.
/// </remarks>
internal sealed class GBufferControlInputs : IDisposable
{
    const int AlbedoUnit = 0, MaterialIdUnit = 2, DepthUnit = 6, DungeonEntranceMaskUnit = 24, HallClipUnit = 10, HallMaskUnit = 26;

    readonly GL _gl;
    readonly uint _plainMode, _depthMode, _empty;

    public GBufferControlInputs(GL gl)
    {
        _gl = gl;
        _plainMode = ConstantTextures.Rgba(gl, 1f, 0f, 0f, 0f);
        _depthMode = ConstantTextures.Rgba(gl, 0f, 0f, 1f, 0f);
        _empty = ConstantTextures.Rgba(gl, 0f, 0f, 0f, 0f);
    }

    public void Bind() => Bind(_plainMode);

    // The mode that draws a hull whole and gives its depth as its colour.
    public void BindDepthOutput() => Bind(_depthMode);

    // Runs draw with what a shrine's hall is clipped by bound, as its G-buffer programs read it: they discard where the entrance mask is one, and in front of
    // the window depth at the clip unit (the game's raw depth of the entrance, which the extracted programs name cTex_SkyOcclusion). The mask is left empty,
    // as the depth already shuts the hall out of everything but the doorway. Other programs read the clip unit as their volume mask, so both units are put
    // back as they were.
    public void WithHallClip(uint clipDepth, Action draw)
    {
        uint clipWas = BoundAt(HallClipUnit), maskWas = BoundAt(HallMaskUnit);
        _gl.BindTextureAt(HallClipUnit, clipDepth);
        _gl.BindTextureAt(HallMaskUnit, _empty);
        draw();
        _gl.BindTextureAt(HallClipUnit, clipWas);
        _gl.BindTextureAt(HallMaskUnit, maskWas);
    }

    uint BoundAt(int unit)
    {
        _gl.ActiveTexture(TextureUnit.Texture0 + unit);
        _gl.GetInteger(GetPName.TextureBinding2D, out int texture);
        return (uint)texture;
    }

    void Bind(uint mode)
    {
        _gl.BindTextureAt(AlbedoUnit, mode);
        foreach (int unit in new[] { MaterialIdUnit, DepthUnit, DungeonEntranceMaskUnit })
            _gl.BindTextureAt(unit, _empty);
    }

    public void Dispose()
    {
        _gl.DeleteTexture(_plainMode);
        _gl.DeleteTexture(_depthMode);
        _gl.DeleteTexture(_empty);
    }
}
