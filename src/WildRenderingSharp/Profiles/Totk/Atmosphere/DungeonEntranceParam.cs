using System.Numerics;
using WildRenderingSharp.Logging;
using WildRenderingSharp.Profiles.Totk.Atmosphere.Palettes;
using WildRenderingSharp.Rom;

namespace WildRenderingSharp.Profiles.Totk.Atmosphere;

/// <summary>The game's <c>DungeonEntranceParam</c>: the light the hall seen through a shrine's doorway is lit with from above and below, and the fog it is drawn through.</summary>
/// <remarks>
/// Read from <c>Pack/Bootup.Nin_NX_NVN.pack.zs</c>, with the values the game ships standing in when the pack is missing. Each light colour is scaled by its own alpha, as the game does
/// before it writes it to the environment block. The fog is a colour with its strength in alpha, a damp, and the distances it starts and ends at. The entrance's pre-shading and
/// resolve passes read the two lights from the environment block in place of the scene's ambient.
/// </remarks>
public sealed record DungeonEntranceParam(Vector3 SkyColor, Vector3 GroundColor, Vector4 FogColor, float FogDamp, float FogStart, float FogEnd)
{
    const string Entry = "Pack/Bootup.Nin_NX_NVN.pack.zs//System/GameGraphics/ModelSceneExtensionParam/Default.game__gfx__ModelSceneExtensionParam.bgyml";

    // The values the game ships, which stand in when the pack is missing.
    public static readonly DungeonEntranceParam Default = new(
        new Vector3(0.176470593f, 0.411764711f, 0.203921601f), new Vector3(0.0666666701f, 0.200000003f, 0.125490203f),
        new Vector4(0f, 0.0313725509f, 0.0196078401f, 0.899999976f), 1f, 0f, 140f);

    public static DungeonEntranceParam Load(IRomAccess? rom)
    {
        if (rom is null || !rom.Exists("Pack/Bootup.Nin_NX_NVN.pack.zs"))
            return Default;
        try
        {
            if (EnvPalette.FromByml(rom.ReadByml(Entry)) is Dictionary<string, object?> root
                && root.TryGetValue("DungeonEntranceParam", out var raw) && raw is Dictionary<string, object?> map)
            {
                Vector4 Color(string key, Vector4 fallback) =>
                    map.TryGetValue(key, out var c) && c is Dictionary<string, object?> rgba
                        ? new Vector4(Channel(rgba, "R", fallback.X), Channel(rgba, "G", fallback.Y), Channel(rgba, "B", fallback.Z), Channel(rgba, "A", fallback.W))
                        : fallback;
                float Number(string key, float fallback) => map.TryGetValue(key, out var v) && v is double d ? (float)d : fallback;

                var sky = Color("SkyColor", new Vector4(Default.SkyColor, 1f));
                var ground = Color("GroundColor", new Vector4(Default.GroundColor, 1f));
                // The game scales each light colour by its own alpha before it writes it to the environment block (ModelSceneExtension::calcRenderView_), so the alpha is the light's intensity.
                return new DungeonEntranceParam(new Vector3(sky.X, sky.Y, sky.Z) * sky.W, new Vector3(ground.X, ground.Y, ground.Z) * ground.W, Color("FogColor", Default.FogColor),
                    Number("FogDamp", Default.FogDamp), Number("FogStart", Default.FogStart), Number("FogEnd", Default.FogEnd));
            }
        }
        catch (Exception ex)
        {
            Log.Warning($"[DungeonEntranceParam] failed to read the entrance's light, using the game's shipped values: {ex.Message}");
        }
        return Default;
    }

    static float Channel(Dictionary<string, object?> color, string key, float fallback) => color.TryGetValue(key, out var v) && v is double d ? (float)d : fallback;
}
