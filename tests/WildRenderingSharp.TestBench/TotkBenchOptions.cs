using WildRenderingSharp;

namespace WildRenderingSharp.TestBench;

/// <summary>The command-line knobs that only mean something to the TotK profile's sky and settings.</summary>
static class TotkBenchOptions
{
    public const string Usage = "[--palette <name>] [--atmosphere <x>] [--tint <0-1>] [--haze <0-1>] [--fog <x>] [--cloud-brightness <x>] " +
        "[--flare-threshold <x>] [--noclouds 1] [--noflare 1] [--nobodies 1] [--list-palettes 1] [--palette-info 1]";

    public static void Apply(WildRenderer renderer, IReadOnlyDictionary<string, string> options)
    {
        var totk = renderer.Totk;
        if (options.TryGetValue("palette", out var palette))
            totk.PaletteName = palette;
        if (options.TryGetValue("atmosphere", out var atmosphere))
            totk.AtmosphereIntensity = float.Parse(atmosphere);
        if (options.TryGetValue("tint", out var tint))
            totk.SkyPaletteTint = float.Parse(tint);
        if (options.TryGetValue("haze", out var haze))
            totk.SkyHorizonHaze = float.Parse(haze);
        if (options.TryGetValue("fog", out var fog))
        {
            totk.UseSkyFog = true;
            totk.SkyFogStrength = float.Parse(fog);
        }
        if (options.TryGetValue("cloud-brightness", out var cloudBrightness))
            totk.CloudBrightness = float.Parse(cloudBrightness);
        if (options.TryGetValue("flare-threshold", out var flareThreshold))
            totk.LensFlareThreshold = float.Parse(flareThreshold);
        if (options.ContainsKey("noclouds"))
            totk.UseRealCloudDome = false;
        if (options.ContainsKey("noflare"))
            totk.UseLensFlare = false;
        if (options.ContainsKey("nobodies"))
            totk.ShowSun = totk.ShowMoon = false;

        if (options.ContainsKey("list-palettes"))
            Console.WriteLine("palettes: " + string.Join(", ", renderer.Environment.Palettes.Names.Order()));
        if (options.ContainsKey("palette-info"))
        {
            var pal = renderer.Environment.Palettes.Get(totk.PaletteName);
            Console.WriteLine($"SkySunColor={pal.SkySunColor} intensity={pal.SkySunColorIntensity} noUse={pal.SkySunColorNoUse} " +
                $"Fog={pal.FogColor} BgDif={pal.BgDifColor}x{pal.BgDifIntensity}");
        }
    }
}
