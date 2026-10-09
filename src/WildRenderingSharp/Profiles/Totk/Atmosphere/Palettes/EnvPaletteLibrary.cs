using WildRenderingSharp.Rom;

namespace WildRenderingSharp.Profiles.Totk.Atmosphere.Palettes;

/// <summary>
/// Loads every <c>game::wm::ResEnvPalette</c> from romfs plus the <c>IconCapture</c> and <c>UI</c> presets (which exist only in
/// code), and resolves a palette by name the way <c>get_palette</c> does.
/// </summary>
public sealed class EnvPaletteLibrary
{
    readonly Dictionary<string, EnvPalette> _byName = new(StringComparer.Ordinal);

    public const string StudioLightPaletteName = "StudioLight";

    public const string ReferenceDaylightPaletteName = "Prequel_MainField_Bluesky_3_Noon";

    public const string DefaultPaletteName = StudioLightPaletteName;

    const string Pack = "Pack/EnvPalette.pack.zs";
    const string EntryDirectory = "WorldMgr/ResEnvPalette";
    const string EntrySuffix = ".game__wm__ResEnvPalette.bgyml";

    public IReadOnlyCollection<string> Names => _byName.Keys;

    EnvPaletteLibrary() { }

    public static EnvPaletteLibrary Empty()
    {
        var lib = new EnvPaletteLibrary();
        lib.AddPreset(StudioLightPaletteName, BuildStudioLightPreset());
        lib.AddPreset("IconCapture", BuildIconCapturePreset());
        lib.AddPreset("UI", BuildUiPreset());
        return lib;
    }

    static Dictionary<string, object?> DeepMerge(
        IReadOnlyDictionary<string, object?> parent, IReadOnlyDictionary<string, object?> child)
    {
        var merged = new Dictionary<string, object?>(parent, StringComparer.Ordinal);
        foreach (var (k, v) in child)
        {
            if (k == "$parent")
                continue;
            if (v is IReadOnlyDictionary<string, object?> childMap
                && merged.TryGetValue(k, out var existing)
                && existing is IReadOnlyDictionary<string, object?> parentMap)
            {
                merged[k] = DeepMerge(parentMap, childMap);
                continue;
            }
            merged[k] = v;
        }
        return merged;
    }

    // Delta palettes name another palette in $parent and override some fields, so they are flattened here with cycle protection.
    public static EnvPaletteLibrary Load(IRomAccess? rom)
    {
        var lib = Empty();
        if (rom is null)
            return lib;
        if (!rom.Exists(Pack))
        {
            Console.WriteLine($"[EnvPaletteLibrary] no {Pack} - only the built-in presets are available.");
            return lib;
        }

        var rawByName = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal);
        foreach (string entry in rom.Enumerate($"{Pack}//{EntryDirectory}", "*" + EntrySuffix))
        {
            string name = Path.GetFileName(entry)[..^EntrySuffix.Length];
            rawByName[name] = (Dictionary<string, object?>)EnvPalette.FromByml(rom.ReadByml(entry))!;
        }

        var resolved = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal);
        Dictionary<string, object?> Resolve(string name, HashSet<string> visiting)
        {
            if (resolved.TryGetValue(name, out var done))
                return done;
            if (!rawByName.TryGetValue(name, out var own) || !visiting.Add(name))
                return own ?? [];

            Dictionary<string, object?> merged;
            if (own.TryGetValue("$parent", out var parentRaw) && parentRaw is string parentGymlPath)
            {
                // "Work/WorldMgr/ResEnvPalette/<Name>.game__wm__ResEnvPalette.gyml" -> "<Name>".
                string parentName = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(parentGymlPath));
                merged = DeepMerge(Resolve(parentName, visiting), own);
            }
            else
            {
                merged = own;
            }
            resolved[name] = merged;
            return merged;
        }

        foreach (string name in rawByName.Keys)
        {
            // Presets win a name collision: they must stay exactly what their builders say, whatever a romfs pack is called.
            if (lib._byName.ContainsKey(name))
            {
                Console.WriteLine($"[EnvPaletteLibrary] romfs palette '{name}' shadows a built-in preset - keeping the preset.");
                continue;
            }
            lib._byName[name] = new EnvPalette(name, Resolve(name, new HashSet<string>(StringComparer.Ordinal)));
        }

        Console.WriteLine($"[EnvPaletteLibrary] loaded {rawByName.Count} palettes from '{Pack}'.");
        return lib;
    }

    void AddPreset(string name, Dictionary<string, object?> raw) => _byName[name] = new EnvPalette(name, raw);

    public EnvPalette Get(string? name)
    {
        if (string.IsNullOrEmpty(name))
            name = DefaultPaletteName;
        if (_byName.TryGetValue(name, out var exact))
            return exact;
        foreach (var (key, pal) in _byName)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
                return pal;
        }
        if (_byName.TryGetValue(DefaultPaletteName, out var fallback))
            return fallback;
        throw new KeyNotFoundException($"Unknown palette '{name}', and not even the built-in '{StudioLightPaletteName}' preset is loaded.");
    }

    // Blank studio lighting: a neutral white key, an untinted ambient and every grade, glow and tint off, so the model's own albedo and emission
    // show. Every shipped palette omits HemiSkyColor and HemiGroundColor, which sends AmbientLighting to its blue-sky fallback, so a neutral ambient
    // needs the pair declared.
    static Dictionary<string, object?> BuildStudioLightPreset() => new()
    {
        ["BgDifColor"] = new object?[] { 1.0, 1.0, 1.0, 1.0 },
        ["BgDifIntensity"] = 5.0,
        ["HemiSkyColor"] = new object?[] { 1.0, 1.0, 1.0, 1.0 },
        ["HemiGroundColor"] = new object?[] { 1.0, 1.0, 1.0, 1.0 },
        ["HemiIntensity"] = 0.35,
        ["AmbientScale"] = 1.0,
        ["VolumeMaskColor"] = new object?[] { 0.0, 0.0, 0.0, 1.0 },
        ["VolumeMaskColorNoUse"] = true,
        ["VolumeMaskIntensity"] = 0.0,
        ["BloomEnable"] = false,
        ["BloomThreshold"] = 1.5,
        ["BloomIntensity"] = 0.0,
        ["BloomClampedLuminance"] = 10.0,
        ["CC_Enable"] = false,
        ["CC_Saturation"] = 1.0,
        ["CC_Brightness"] = 1.0,
        ["CC_Gamma"] = 1.0,
        ["CC_Hue"] = 0.0,
    };

    static Dictionary<string, object?> BuildIconCapturePreset() => new()
    {
        ["BgDifColor"] = new object?[] { 1.0, 0.949999988079071, 0.75, 1.0 },
        ["BgDifIntensity"] = 5.5,
        ["HemiSkyColor"] = new object?[] { 0.6000000238418579, 0.75, 1.0, 1.0 },
        ["HemiGroundColor"] = new object?[] { 1.0, 0.8799999952316284, 0.6700000166893005, 1.0 },
        ["HemiIntensity"] = 0.35,
        ["VolumeMaskColor"] = new object?[] { 0.0, 0.0, 0.0, 1.0 },
        ["VolumeMaskIntensity"] = 0.0,
        ["BloomEnable"] = false,
        ["BloomThreshold"] = 1.5,
        ["BloomIntensity"] = 0.0,
        ["BloomClampedLuminance"] = 10.0,
        ["CC_Enable"] = true,
        ["CC_Saturation"] = 1.17499995,
        ["CC_Brightness"] = 1.0,
        ["CC_Gamma"] = 1.0,
        ["CC_Hue"] = 0.0,
    };

    static Dictionary<string, object?> BuildUiPreset() => new()
    {
        ["BgDifColor"] = new object?[] { 1.0, 0.949999988079071, 0.75, 1.0 },
        ["BgDifIntensity"] = 5.0,
        ["HemiSkyColor"] = new object?[] { 0.39800000190734863, 0.7379999756813049, 1.0, 1.0 },
        ["HemiGroundColor"] = new object?[] { 1.0, 0.8799999952316284, 0.6700000166893005, 1.0 },
        ["HemiIntensity"] = 0.35,
        ["VolumeMaskColor"] = new object?[] { 0.0, 0.0, 0.0, 1.0 },
        ["VolumeMaskIntensity"] = 0.0,
        ["BloomEnable"] = false,
        ["BloomThreshold"] = 1.5,
        ["BloomIntensity"] = 0.0,
        ["BloomClampedLuminance"] = 10.0,
        ["CC_Enable"] = true,
        ["CC_Saturation"] = 1.17499995,
        ["CC_Brightness"] = 1.0,
        ["CC_Gamma"] = 1.0,
        ["CC_Hue"] = 0.0,
    };
}
