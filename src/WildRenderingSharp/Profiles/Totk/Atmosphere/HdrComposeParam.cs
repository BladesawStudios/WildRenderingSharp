using WildRenderingSharp.Logging;
using WildRenderingSharp.Profiles.Totk.Atmosphere.Palettes;
using WildRenderingSharp.Rom;

namespace WildRenderingSharp.Profiles.Totk.Atmosphere;

/// <summary>
/// The game's <c>HdrComposeParam</c>, from <c>Pack/Bootup.Nin_NX_NVN.pack.zs</c>: the saturation the HDR compose applies to the dark end and to the bright end
/// of the tonemapped image. The compose shader's <c>cParam.x</c> is the bottom and <c>cParam.y</c> the rise from it to the top.
/// </summary>
public sealed record HdrComposeParam(float SaturationBottom, float SaturationTop)
{
    const string Entry = "Pack/Bootup.Nin_NX_NVN.pack.zs//System/GameGraphics/ModelSceneExtensionParam/Default.game__gfx__ModelSceneExtensionParam.bgyml";

    // The values the game ships, which stand in when the pack is missing.
    public static readonly HdrComposeParam Default = new(0.99f, 1.17f);

    public static HdrComposeParam Load(IRomAccess? rom)
    {
        if (rom is null || !rom.Exists("Pack/Bootup.Nin_NX_NVN.pack.zs"))
            return Default;
        try
        {
            if (EnvPalette.FromByml(rom.ReadByml(Entry)) is Dictionary<string, object?> root
                && root.TryGetValue("HdrComposeParam", out var raw) && raw is Dictionary<string, object?> map
                && map.TryGetValue("SaturationBottom", out var bottom) && map.TryGetValue("SaturationTop", out var top)
                && bottom is double b && top is double t)
                return new HdrComposeParam((float)b, (float)t);
        }
        catch (Exception ex)
        {
            Log.Warning($"[HdrComposeParam] failed to read the compose saturation, using the game's shipped values: {ex.Message}");
        }
        return Default;
    }
}
