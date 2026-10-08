
namespace WildRenderingSharp.Profiles.Totk.Terrain;

/// <summary>A host whose terrain the renderer shades - see <see cref="TerrainShading"/>.</summary>
public interface ITerrainHost
{
    /// <summary>Changes whenever what <see cref="DrawShadow"/> draws would change - tiles loaded, edited.</summary>
    long ShadowVersion { get; }

    /// <summary>Draws the terrain into the G-buffer, which is bound with the state set (see <see cref="TerrainShading"/>'s units).</summary>
    void DrawGBuffer(TerrainDraw draw);

    /// <summary>Draws the terrain's depth into a shadow cascade, which is bound, with the light's <c>Context</c> at 1.</summary>
    void DrawShadow(TerrainDraw draw);

    /// <summary>Whether the host draws its water through the game's water program this frame (see <see cref="TerrainShading.LinkWaterProgram"/>).</summary>
    bool HasWater => false;

    /// <summary>
    /// Draws the water with the program from <see cref="TerrainShading.LinkWaterProgram"/> - or, when
    /// <paramref name="stamp"/>, the same geometry with the one from
    /// <see cref="TerrainShading.LinkWaterStampProgram"/>, which marks its pixels for the water's
    /// deferred pass. Everything but the host's own textures and <c>TerrainSystem</c> is bound.
    /// </summary>
    void DrawWater(TerrainDraw draw, bool stamp) { }
}
