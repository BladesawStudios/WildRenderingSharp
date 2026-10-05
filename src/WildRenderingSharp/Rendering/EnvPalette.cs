using System.Numerics;
using System.Text.Json;
using BymlLibrary;
using SarcLibrary;

namespace WildRenderingSharp.Rendering;

/// <summary>
/// A <c>game::wm::ResEnvPalette</c> record - the per-time-of-day/weather parameter set the
/// runtime actually feeds the renderer, loaded straight from romfs (<c>Pack/EnvPalette.pack.zs</c>
/// -&gt; <c>WorldMgr/ResEnvPalette/*.bgyml</c> - see <see cref="EnvPaletteLibrary.LoadFromRomfs"/>).
///
/// The source data is genuinely loosely typed (some palettes omit fields others have and instead
/// inherit them via <c>$parent</c>, already resolved by the time this class sees them; colours
/// come through as a <c>{R,G,B,A}</c> map, not a fixed schema) so this wraps a plain key/value bag
/// rather than a rigid schema, with named accessors for exactly the fields the render pipeline
/// consumes. Two presets - <c>IconCapture</c>/<c>UI</c> - exist only in code (no romfs bgyml for
/// either) and use a plain <c>[r,g,b,a]</c> array for colours instead; <see cref="TryGetColor"/>
/// accepts both shapes.
/// </summary>
public sealed class EnvPalette
{
    readonly IReadOnlyDictionary<string, object?> _raw;

    public string Name { get; }

    public EnvPalette(string name, IReadOnlyDictionary<string, object?> raw)
    {
        Name = name;
        _raw = raw;
    }

    /// <summary>Every field name this palette carries after <c>$parent</c> inheritance is flattened - for diagnostics and for auditing which authored values the pipeline does and does not consume.</summary>
    public IReadOnlyCollection<string> Keys => _raw.Keys.ToArray();

    public bool TryGetFloat(string key, out float value)
    {
        if (_raw.TryGetValue(key, out var raw))
        {
            switch (raw)
            {
                case double d: value = (float)d; return true;
                case string s when float.TryParse(s, out var f): value = f; return true;
            }
        }
        value = default;
        return false;
    }

    public float GetFloat(string key, float fallback) => TryGetFloat(key, out var v) ? v : fallback;

    public bool GetBool(string key, bool fallback) => _raw.TryGetValue(key, out var raw) && raw is bool b ? b : fallback;

    public bool TryGetColor(string key, out Vector4 value)
    {
        if (_raw.TryGetValue(key, out var raw))
        {
            // The two hardcoded presets (IconCapture/UI) use a plain [r,g,b,a] array; every real
            // romfs ResEnvPalette stores a colour as a {R,G,B,A} map instead (see FromByml) -
            // support both rather than picking one and breaking the other.
            if (raw is object?[] { Length: >= 3 } arr)
            {
                float w = arr.Length >= 4 ? ToFloat(arr[3]) : 1f;
                value = new Vector4(ToFloat(arr[0]), ToFloat(arr[1]), ToFloat(arr[2]), w);
                return true;
            }
            if (raw is IReadOnlyDictionary<string, object?> map && map.ContainsKey("R"))
            {
                map.TryGetValue("A", out var a);
                value = new Vector4(ToFloat(map["R"]), ToFloat(map.GetValueOrDefault("G")), ToFloat(map.GetValueOrDefault("B")), a is null ? 1f : ToFloat(a));
                return true;
            }
        }
        value = default;
        return false;
    }

    public Vector4 GetColor(string key, Vector4 fallback) => TryGetColor(key, out var v) ? v : fallback;

    static float ToFloat(object? v) => v switch
    {
        double d => (float)d,
        string s when float.TryParse(s, out var f) => f,
        _ => 0f,
    };

    // ---- named accessors for the fields the pipeline actually reads ----

    static Vector3 Xyz(Vector4 v) => new(v.X, v.Y, v.Z);

    public Vector3 BgDifColor => Xyz(GetColor("BgDifColor", new Vector4(1, 1, 1, 0)));
    public float BgDifIntensity => GetFloat("BgDifIntensity", 5.0f);

    public bool HasHemiColors => _raw.ContainsKey("HemiSkyColor") && _raw.ContainsKey("HemiGroundColor");
    public Vector3 HemiSkyColor => Xyz(GetColor("HemiSkyColor", Vector4.Zero));
    public Vector3 HemiGroundColor => Xyz(GetColor("HemiGroundColor", Vector4.Zero));
    public float HemiIntensity => GetFloat("HemiIntensity", 1.0f);

    /// <summary>
    /// A direct multiplier on the hemisphere ambient, authored by 44 of the 131 shipped palettes
    /// (absent = 1.0, the neutral). Multiplies - does not replace - the viewer's own ambient
    /// slider, so a palette that declares it shifts the ambient without taking the control away.
    /// </summary>
    public float AmbientScale => GetFloat("AmbientScale", 1f);

    // ---- sky scattering: what the game feeds its own sky shader, and what WildRenderingSharp uses to
    // approximate the sky irradiance the hemisphere ambient should be (see AmbientLighting).

    /// <summary>How much blue Rayleigh scattering this sky has: 1.0 at overworld noon, 0.25 at night, 0.0 under a blood moon.</summary>
    public float SkyRayleighAmplifier => GetFloat("SkyRParam_rayleigh_amplifier", 1f);
    /// <summary>How much sun-coloured Mie haze this sky has: 12 at overworld noon, 0 at night, 256 under a blood moon.</summary>
    public float SkyMieAmplifier => GetFloat("SkyRParam_mie_amplifier", 0f);
    /// <summary>The Mie phase asymmetry. Carried for completeness; the ambient approximation uses only the amplifiers and the sun colour.</summary>
    public float SkyMieSymmetrical => GetFloat("SkyRParam_mie_symmetrical", 0f);
    /// <summary>The sun's own colour as the SKY sees it - the tint of the Mie haze term, and a different field from <see cref="BgDifColor"/>, which is the directional light that hits surfaces.</summary>
    public Vector3 SkySunColor => Xyz(GetColor("SkySunColor", new Vector4(1, 1, 1, 1)));
    public float SkySunColorIntensity => GetFloat("SkySunColorIntensity", 1f);
    /// <summary>The palette's own "ignore my SkySunColor" switch.</summary>
    public bool SkySunColorNoUse => GetBool("SkySunColorNoUse", false);

    /// <summary>The palette's own authored distance-fog colour - also used by <c>Pipeline.BackgroundPass</c> as the real-data horizon haze tint for the TotK Sky background (real atmospheric games blend toward their fog colour near the horizon; this is that same authored value, not an invented one).</summary>
    public Vector3 FogColor => Xyz(GetColor("FogColor", new Vector4(0.6f, 0.75f, 1f, 0f)));

    /// <summary>The palette's own authored fog far distance. Confirmed real: a captured frame's own cloud-shader constant buffer holds <c>1/FogEnd</c> (1/300 for the palette that capture used) - see <see cref="Pipeline.CloudDomePass"/>.</summary>
    public float FogEnd => GetFloat("FogEnd", 300f);
    public float FogStart => GetFloat("FogStart", 0f);

    /// <summary>
    /// <c>FogColor</c>'s alpha - the adhoc-fog DENSITY, and the palette's real on/off for the
    /// horizon haze band. Noon authors 0 (clear sky, no band); BloodyMoon_DarknessDragon authors
    /// 0.6 with a pure-red <c>FogColor</c>, which is the band the game shows under a blood moon.
    /// </summary>
    public float FogDensity => GetColor("FogColor", new Vector4(0.6f, 0.75f, 1f, 0f)).W;

    /// <summary>"Af" = Adhoc Fog - these are the per-palette counterparts of <c>master_field.baglsky</c>'s own <c>adhoc_fog_atten_sky</c>/<c>_grd</c>. See <c>SkyPostFxPass</c> for how the sky one is applied (and what about it is still unconfirmed).</summary>
    public float AdhocFogAttenSky => GetFloat("AfParam_attenuationForSky", 1f);
    public float AdhocFogAttenGrd => GetFloat("AfParam_attenuationForGrd", 4f);

    /// <summary>Height fog, authored per palette (<c>YFogColor</c>/<c>YFogStart</c>). Parsed but not yet consumed - see CLAUDE.md.</summary>
    public Vector3 YFogColor => Xyz(GetColor("YFogColor", Vector4.Zero));
    public float YFogDensity => GetColor("YFogColor", Vector4.Zero).W;
    public float YFogStart => GetFloat("YFogStart", 0f);
    /// <summary>Real authored scatter-fog attenuation (<c>SfParam_attenuation</c>) - confirmed present verbatim in a real captured cloud-shader constant buffer.</summary>
    public float ScatterFogAttenuation => GetFloat("SfParam_attenuation", 40f);
    /// <summary>Real authored scatter-fog horizon exponent (<c>SfParam_horizontal</c>) - likewise confirmed verbatim in a real capture.</summary>
    public float ScatterFogHorizontal => GetFloat("SfParam_horizontal", 2.5f);

    /// <summary>
    /// One of the palette's two authored cloud layers (<c>Cloud0</c>/<c>Cloud1</c> - see
    /// <see cref="Cloud0"/>/<see cref="Cloud1"/>) - real authored per-preset cloud shading data:
    /// a classic 3-way gradient (shadow/base/hilight, each with its own colour AND intensity) plus
    /// a backlight term for when the sun sits behind the cloud. <see cref="Present"/> is false when
    /// the layer is either entirely absent from this palette OR explicitly disabled via its own
    /// <c>Cloud0NoUse</c>/<c>Cloud1NoUse</c> switch (both real, authored per-palette - e.g. a clear
    /// "Bluesky" noon preset authors real Cloud0 data but sets <c>Cloud0NoUse: true</c>, so only
    /// its Cloud1 layer is actually meant to show).
    /// </summary>
    public readonly record struct CloudLayer(
        bool Present, float BacklightPower,
        Vector3 ColorBackLight, Vector3 ColorBase, Vector3 ColorHilight, Vector3 ColorShadow,
        float IntensityBase, float IntensityHilight, float IntensityShadow);

    CloudLayer GetCloudLayer(string key)
    {
        if (GetBool(key + "NoUse", false))
            return default;
        if (!_raw.TryGetValue(key, out var raw) || raw is not IReadOnlyDictionary<string, object?> map)
            return default;
        var sub = new EnvPalette(key, map);
        return new CloudLayer(
            Present: true,
            BacklightPower: sub.GetFloat("BacklightPower", 1f),
            ColorBackLight: Xyz(sub.GetColor("ColorBackLight", Vector4.One)),
            ColorBase: Xyz(sub.GetColor("ColorBase", Vector4.One)),
            ColorHilight: Xyz(sub.GetColor("ColorHilight", Vector4.One)),
            ColorShadow: Xyz(sub.GetColor("ColorShadow", Vector4.One)),
            IntensityBase: sub.GetFloat("IntensityBase", 1f),
            IntensityHilight: sub.GetFloat("IntensityHilight", 1f),
            IntensityShadow: sub.GetFloat("IntensityShadow", 1f));
    }

    public CloudLayer Cloud0 => GetCloudLayer("Cloud0");
    public CloudLayer Cloud1 => GetCloudLayer("Cloud1");

    public Vector3 VolumeMaskColor => Xyz(GetColor("VolumeMaskColor", Vector4.Zero));
    public float VolumeMaskIntensity => GetFloat("VolumeMaskIntensity", 0f);
    /// <summary>
    /// The palette's own "ignore my VolumeMaskColor" switch. Honoured, though note the tint it
    /// gates is inert in WildRenderingSharp for a different reason: the resolve passes apply it as
    /// <c>cTex_VolumeMask.z * Env[81].w</c>, and WildRenderingSharp binds an all-zero 1x1 VolumeMask texture
    /// (the correct neutral for "not inside a fog/indoor volume"), so the .z factor is 0 whatever
    /// the palette says. Driving it for real needs the volume-mask buffer the game's own indoor
    /// system fills, which WildRenderingSharp does not render.
    /// </summary>
    public bool VolumeMaskColorNoUse => GetBool("VolumeMaskColorNoUse", false);

    public bool BloomEnable => GetBool("BloomEnable", true);
    public float BloomIntensity => GetFloat("BloomIntensity", 0f);
    public float BloomThreshold => GetFloat("BloomThreshold", 1.5f);
    public float BloomClampedLuminance => GetFloat("BloomClampedLuminance", 30f);

    public bool ColorCorrectEnable => GetBool("CC_Enable", true);
    public float ColorCorrectSaturation => GetFloat("CC_Saturation", 1.0f);
    public float ColorCorrectBrightness => GetFloat("CC_Brightness", 1.0f);
    public float ColorCorrectGamma => GetFloat("CC_Gamma", 1.0f);

    /// <summary>Parses one JSON value into the plain-CLR-object bag <see cref="EnvPalette"/> reads from - used only by the two hardcoded presets below now that real palettes load straight from romfs (<see cref="EnvPaletteLibrary.LoadFromRomfs"/>).</summary>
    internal static object? FromJson(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString(),
        JsonValueKind.Number => el.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Array => el.EnumerateArray().Select(FromJson).ToArray(),
        JsonValueKind.Object => el.EnumerateObject().ToDictionary(p => p.Name, p => FromJson(p.Value)),
        _ => null,
    };

    /// <summary>
    /// Parses one <c>BymlLibrary.Byml</c> node into the same plain-CLR-object bag - a colour comes
    /// through as a <c>{R,G,B,A}</c> map (not an array like the JSON presets use), which is why
    /// <see cref="TryGetColor"/> accepts both shapes.
    /// </summary>
    internal static object? FromByml(Byml node) => node.Type switch
    {
        BymlNodeType.String => node.GetString(),
        BymlNodeType.Bool => node.GetBool(),
        BymlNodeType.Int => (double)node.GetInt(),
        BymlNodeType.UInt32 => (double)node.GetUInt32(),
        BymlNodeType.Int64 => (double)node.GetInt64(),
        BymlNodeType.UInt64 => (double)node.GetUInt64(),
        BymlNodeType.Float => (double)node.GetFloat(),
        BymlNodeType.Double => node.GetDouble(),
        BymlNodeType.Array => node.GetArray().Select(FromByml).ToArray(),
        BymlNodeType.Map => node.GetMap().ToDictionary(p => p.Key, p => FromByml(p.Value), StringComparer.Ordinal),
        _ => null,
    };
}

/// <summary>
/// Loads every <c>game::wm::ResEnvPalette</c> straight out of romfs (<c>Pack/EnvPalette.pack.zs</c>
/// -&gt; <c>WorldMgr/ResEnvPalette/*.bgyml</c>, via <c>TotkCommon</c>/<c>SarcLibrary</c>/
/// <c>BymlLibrary</c>) plus the two hardcoded icon-capture presets (<c>IconCapture</c>/<c>UI</c> -
/// these exist only in code, romfs has no equivalent bgyml for them), and resolves a palette by
/// name the same fuzzy way <c>get_palette</c> does.
/// </summary>
public sealed class EnvPaletteLibrary
{
    readonly Dictionary<string, EnvPalette> _byName = new(StringComparer.Ordinal);

    /// <summary>
    /// WildRenderingSharp's own neutral studio preset (see <see cref="BuildStudioLightPreset"/>) and the
    /// default everything falls back to. Deliberately NOT a romfs palette: it exists with no romfs
    /// configured at all, and a shader-suite session should open on lighting that shows a model as
    /// it is rather than on a time-of-day grade someone has to recognise and undo first.
    /// </summary>
    public const string StudioLightPaletteName = "StudioLight";

    /// <summary>The romfs palette the game itself uses for overworld noon - the reference to compare a studio render against, and what <see cref="StudioLightPaletteName"/> replaced as the startup default.</summary>
    public const string ReferenceDaylightPaletteName = "Prequel_MainField_Bluesky_3_Noon";

    public const string DefaultPaletteName = StudioLightPaletteName;

    const string EntryPrefix = "WorldMgr/ResEnvPalette/";
    const string EntrySuffix = ".game__wm__ResEnvPalette.bgyml";

    public IReadOnlyCollection<string> Names => _byName.Keys;

    EnvPaletteLibrary() { }

    /// <summary>
    /// Presets only, no romfs data - the safe starting point before a romfs path is configured.
    /// <see cref="Get"/> keeps working here, because <see cref="DefaultPaletteName"/> is itself one
    /// of these presets rather than a romfs palette that may not have loaded.
    /// </summary>
    public static EnvPaletteLibrary Empty()
    {
        var lib = new EnvPaletteLibrary();
        lib.AddPreset(StudioLightPaletteName, BuildStudioLightPreset());
        lib.AddPreset("IconCapture", BuildIconCapturePreset());
        lib.AddPreset("UI", BuildUiPreset());
        return lib;
    }

    /// <summary>
    /// Loads every ResEnvPalette from <paramref name="romfsRoot"/>. Returns <see cref="Empty"/>
    /// (with a log line, not an exception) if <c>Pack/EnvPalette.pack.zs</c> isn't found there -
    /// romfs root not yet configured, or pointed at the wrong folder.
    ///
    /// Many palettes are deltas: <c>$parent</c> names another ResEnvPalette (by its romfs path,
    /// e.g. <c>Work/WorldMgr/ResEnvPalette/Ganondorf_Castle_1....gyml</c>) whose OWN fields this
    /// one inherits, overriding only the handful it actually declares - resolved here once at
    /// load time (with cycle protection) so every <see cref="EnvPalette"/> this library hands out
    /// already has its complete, flattened field set.
    /// </summary>

    /// <summary>
    /// Merges a delta palette over its parent, RECURSING into nested maps.
    /// </summary>
    /// <remarks>
    /// A shallow merge is wrong here and quietly so. Delta palettes routinely override only a
    /// couple of fields of a nested object - BloodyMoon_DarknessDragon's Cloud1 carries just
    /// BacklightPower/IntensityBase/IntensityHilight - so replacing the whole object throws away
    /// every field the delta did not mention, including all three of the parent's cloud COLOURS.
    /// Those then fall back to (1,1,1) and the clouds render white instead of the authored deep
    /// red. Nothing errors; the palette simply loses data.
    /// </remarks>
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

    public static EnvPaletteLibrary LoadFromRomfs(string? romfsRoot)
    {
        var lib = Empty();
        if (string.IsNullOrEmpty(romfsRoot))
            return lib;

        string pkgPath = Path.Combine(romfsRoot, "Pack", "EnvPalette.pack.zs");
        if (!File.Exists(pkgPath))
        {
            Console.WriteLine($"[EnvPaletteLibrary] no EnvPalette.pack.zs under '{romfsRoot}' - only the built-in presets are available.");
            return lib;
        }

        TotkCommon.Totk.Config.GamePath = romfsRoot;
        byte[] raw = File.ReadAllBytes(pkgPath);
        byte[] decompressed = TotkCommon.Zstd.IsCompressed(raw) ? TotkCommon.Totk.Zstd.Decompress(raw) : raw;
        var sarc = Sarc.FromBinary(new ArraySegment<byte>(decompressed));

        var rawByName = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal);
        foreach (var (path, data) in sarc)
        {
            if (!path.StartsWith(EntryPrefix, StringComparison.Ordinal) || !path.EndsWith(EntrySuffix, StringComparison.Ordinal))
                continue;
            string name = path[EntryPrefix.Length..^EntrySuffix.Length];
            byte[] bymlBytes = data.ToArray();
            if (TotkCommon.Zstd.IsCompressed(bymlBytes))
                bymlBytes = TotkCommon.Totk.Zstd.Decompress(bymlBytes);
            var map = (Dictionary<string, object?>)EnvPalette.FromByml(Byml.FromBinary(bymlBytes))!;
            rawByName[name] = map;
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
            // Presets win a name collision: StudioLight/IconCapture/UI are WildRenderingSharp's own and must
            // stay exactly what BuildXPreset says, whatever a romfs pack happens to be called.
            if (lib._byName.ContainsKey(name))
            {
                Console.WriteLine($"[EnvPaletteLibrary] romfs palette '{name}' shadows a built-in preset - keeping the preset.");
                continue;
            }
            lib._byName[name] = new EnvPalette(name, Resolve(name, new HashSet<string>(StringComparer.Ordinal)));
        }

        Console.WriteLine($"[EnvPaletteLibrary] loaded {rawByName.Count} palettes from '{pkgPath}'.");
        return lib;
    }

    void AddPreset(string name, Dictionary<string, object?> raw) => _byName[name] = new EnvPalette(name, raw);

    /// <summary>
    /// Mirrors <c>get_palette</c>: exact match, then case-insensitive, across the presets first and
    /// then the loaded palettes, falling back to <see cref="DefaultPaletteName"/> - which is the
    /// built-in <see cref="StudioLightPaletteName"/> preset and therefore always present, even with
    /// no romfs configured yet - rather than throwing. A missing or not-yet-configured romfs
    /// shouldn't be able to crash rendering.
    /// </summary>
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

    /// <summary>
    /// Blank studio lighting: a neutral white key, a neutral untinted ambient, and every grade,
    /// glow and tint switched off. Not a real game palette and not pretending to be one - the
    /// point is that nothing here colours or reshapes what the model itself contributes, so a
    /// material's own albedo/emission is what you are looking at.
    ///
    /// Every shipped romfs palette omits <c>HemiSkyColor</c>/<c>HemiGroundColor</c> entirely, which
    /// sends <see cref="WildRenderingSharp.Rendering.AmbientLighting"/> to its blue-sky Rayleigh fallback
    /// - so a neutral ambient is only reachable by declaring the pair explicitly, as this does.
    /// The magnitudes (BgDifIntensity 5.0, HemiIntensity 0.35) are the shipped icon-capture
    /// preset's, which are already calibrated against this pipeline's exposure; only the hues are
    /// neutralised.
    /// </summary>
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
