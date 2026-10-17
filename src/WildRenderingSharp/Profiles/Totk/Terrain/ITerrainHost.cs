
namespace WildRenderingSharp.Profiles.Totk.Terrain;

/// <summary>A host whose terrain the renderer shades - see <see cref="TerrainShading"/>.</summary>
public interface ITerrainHost
{
    long ShadowVersion { get; }

    // What a cascade over the sphere holds of the terrain: a host whose detail changes with the camera may leave out changes the cascade cannot show.
    long ShadowVersionNear(System.Numerics.Vector3 centre, float radius) => ShadowVersion;

    void DrawGBuffer(TerrainDraw draw);

    void DrawShadow(TerrainDraw draw);

    bool HasWater => false;

    void DrawWater(TerrainDraw draw, bool stamp) { }
}
