
namespace WildRenderingSharp.Profiles.Totk.Terrain;

/// <summary>A host whose terrain the renderer shades - see <see cref="TerrainShading"/>.</summary>
public interface ITerrainHost
{
    long ShadowVersion { get; }

    void DrawGBuffer(TerrainDraw draw);

    void DrawShadow(TerrainDraw draw);

    bool HasWater => false;

    void DrawWater(TerrainDraw draw, bool stamp) { }
}
