using BymlLibrary;
using SarcLibrary;

namespace WildRenderingSharp.Profiles.Totk.Atmosphere;

/// <summary>
/// The weather's own cloud data from <c>Pack/Bootup.Nin_NX_NVN.pack.zs</c>: per-layer motion (<c>PrequelPrCloud</c>, three layers) and the look of each
/// cloud weather (<c>PrequelCwCloud</c>, three weathers by two layers). Loaded once and cached.
/// </summary>
public sealed class CloudWeather
{
    public const int LayerCount = 3;
    public const int WeatherCount = 3;
    public const int LookLayerCount = 2;

    const string MotionPrefix = "WorldMgr/PrequelPrCloud/";
    const string LookPrefix = "WorldMgr/PrequelCwCloud/";

    static CloudWeather? _cached;

    public CloudMotionLayer?[] Motion { get; } = new CloudMotionLayer?[LayerCount];

    public CloudLookLayer?[,] Looks { get; } = new CloudLookLayer?[WeatherCount, LookLayerCount];

    public static readonly CloudWeather Empty = new();

    public static CloudWeather LoadFromRomfs(string? romfsRoot)
    {
        if (_cached is not null)
            return _cached;
        if (string.IsNullOrEmpty(romfsRoot))
            return Empty;

        string path = Path.Combine(romfsRoot, "Pack", "Bootup.Nin_NX_NVN.pack.zs");
        if (!File.Exists(path))
        {
            Console.WriteLine($"[CloudWeather] no Bootup pack under '{romfsRoot}' - clouds use the postfx baseline only.");
            return Empty;
        }

        try
        {
            byte[] raw = File.ReadAllBytes(path);
            byte[] data = TotkCommon.Zstd.IsCompressed(raw) ? TotkCommon.Totk.Zstd.Decompress(raw) : raw;
            var sarc = Sarc.FromBinary(new ArraySegment<byte>(data));

            var weather = new CloudWeather();
            foreach (var (entry, bytes) in sarc)
            {
                if (entry.StartsWith(MotionPrefix, StringComparison.Ordinal))
                {
                    // "00N.game__wm__PrequelPrCloud.bgyml": N is the layer.
                    int layer = entry[MotionPrefix.Length + 2] - '0';
                    if ((uint)layer < LayerCount)
                        weather.Motion[layer] = CloudMotionLayer.FromParams(ReadMap(bytes));
                }
                else if (entry.StartsWith(LookPrefix, StringComparison.Ordinal))
                {
                    // "NNN_L.game__wm__PrequelCwCloud.bgyml": NNN is the weather, L the layer.
                    string name = entry[LookPrefix.Length..];
                    int set = name[2] - '0', layer = name[4] - '0';
                    if ((uint)set < WeatherCount && (uint)layer < LookLayerCount)
                        weather.Looks[set, layer] = new CloudLookLayer(ReadMap(bytes));
                }
            }

            Console.WriteLine("[CloudWeather] loaded the weather cloud motion and looks from the Bootup pack.");
            return _cached = weather;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CloudWeather] failed to read the weather cloud data, using the postfx baseline: {ex.Message}");
            return Empty;
        }
    }

    static IReadOnlyDictionary<string, object?> ReadMap(ReadOnlyMemory<byte> bytes)
    {
        byte[] data = bytes.ToArray();
        if (TotkCommon.Zstd.IsCompressed(data))
            data = TotkCommon.Totk.Zstd.Decompress(data);
        return (Dictionary<string, object?>)EnvPalette.FromByml(Byml.FromBinary(data))!;
    }
}
