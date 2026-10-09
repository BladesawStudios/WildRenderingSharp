using BymlLibrary;
using WildRenderingSharp.Profiles.Totk.Atmosphere.Palettes;
using WildRenderingSharp.Rom;

namespace WildRenderingSharp.Profiles.Totk.Atmosphere.Clouds;

/// <summary>
/// The weather's own cloud data from <c>Pack/Bootup.Nin_NX_NVN.pack.zs</c>: per-layer motion (<c>PrequelPrCloud</c>, three layers) and the look of each
/// cloud weather (<c>PrequelCwCloud</c>, three weathers by two layers).
/// </summary>
public sealed class CloudWeather
{
    public const int LayerCount = 3;
    public const int WeatherCount = 3;
    public const int LookLayerCount = 2;

    const string Pack = "Pack/Bootup.Nin_NX_NVN.pack.zs";
    const string MotionDirectory = "WorldMgr/PrequelPrCloud";
    const string LookDirectory = "WorldMgr/PrequelCwCloud";

    public CloudMotionLayer?[] Motion { get; } = new CloudMotionLayer?[LayerCount];

    public CloudLookLayer?[,] Looks { get; } = new CloudLookLayer?[WeatherCount, LookLayerCount];

    public static readonly CloudWeather Empty = new();

    public static CloudWeather Load(IRomAccess? rom)
    {
        if (rom is null)
            return Empty;
        if (!rom.Exists(Pack))
        {
            Console.WriteLine($"[CloudWeather] no {Pack} - clouds use the postfx baseline only.");
            return Empty;
        }

        try
        {
            var weather = new CloudWeather();
            foreach (string entry in rom.Enumerate($"{Pack}//{MotionDirectory}"))
            {
                // "00N.game__wm__PrequelPrCloud.bgyml": N is the layer.
                int layer = Path.GetFileName(entry)[2] - '0';
                if ((uint)layer < LayerCount)
                    weather.Motion[layer] = CloudMotionLayer.FromParams(ReadMap(rom, entry));
            }
            foreach (string entry in rom.Enumerate($"{Pack}//{LookDirectory}"))
            {
                // "NNN_L.game__wm__PrequelCwCloud.bgyml": NNN is the weather, L the layer.
                string name = Path.GetFileName(entry);
                int set = name[2] - '0', layer = name[4] - '0';
                if ((uint)set < WeatherCount && (uint)layer < LookLayerCount)
                    weather.Looks[set, layer] = new CloudLookLayer(ReadMap(rom, entry));
            }

            Console.WriteLine("[CloudWeather] loaded the weather cloud motion and looks from the Bootup pack.");
            return weather;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CloudWeather] failed to read the weather cloud data, using the postfx baseline: {ex.Message}");
            return Empty;
        }
    }

    static IReadOnlyDictionary<string, object?> ReadMap(IRomAccess rom, string entry) =>
        (Dictionary<string, object?>)EnvPalette.FromByml(rom.ReadByml(entry))!;
}
