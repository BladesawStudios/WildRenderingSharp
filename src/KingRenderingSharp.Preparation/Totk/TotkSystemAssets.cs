using ShaderLibrary.CompileTool;
using KingRenderingSharp.Core.Logging;
using KingRenderingSharp.Preparation.Totk.Effects;
using KingRenderingSharp.Games.Totk.Atmosphere.Palettes;
using KingRenderingSharp.Core.Storage;

namespace KingRenderingSharp.Preparation.Totk;

/// <summary>
/// Builds the assets every TotK model shares, once per cache, from the bare ROM. A failing step costs a feature, not the renderer, so only
/// the HDR compose shader is required.
/// </summary>
public static class TotkSystemAssets
{
    const string DeferredModel = "SystemModel.DeferredMain";

    public static void Ensure(TotkRomfs romfs, CacheLayout cache, Action<string>? log)
    {
        var failures = new List<string>();
        void Step(string name, Action step) => Run(failures, log, name, step);
        Step("system shaders", () => Once(Path.Combine(cache.Shaders, "agl_hdr_compose.vert"), cache.Shaders, () => TotkAglShaders.HdrCompose(romfs.Base, cache.Shaders)));
        Step("gloom shaders", () => Once(Path.Combine(cache.Shaders, "agl_miasma_renderer_compose.frag"), cache.Shaders, () => TotkAglShaders.Gloom(romfs.Base, cache.Shaders)));
        Step("lens flare", () => Once(Path.Combine(cache.Shaders, "agl_flare_filter_flare.frag"), cache.Shaders, () => TotkAglShaders.LensFlare(romfs.Base, cache.Shaders)));
        Step("cloud shader", () => Once(Path.Combine(cache.Shaders, "agl_cloud.frag"), cache.Shaders, () => TotkAglShaders.Cloud(romfs.Base, cache.Shaders)));
        // Checks the newest file this extractor produces, so a cache built before the adhoc-fog variant existed gains it.
        Step("sky shaders", () => Once(Path.Combine(cache.Shaders, "agl_sky_postfx_sky_fog.frag"), cache.Shaders, () => TotkAglShaders.SkyPostFx(romfs.Base, cache.Shaders)));
        Step("terrain water", () =>
        {
            if (!TotkTerrainWater.IsExported(cache.Shaders))
                TotkTerrainWater.Export(romfs.Base, cache.Shaders);
        });
        Step("field haze", () =>
        {
            if (!TotkFieldHazeEffect.IsExported(cache))
                TotkFieldHazeEffect.Export(romfs.Base, cache);
        });
        Step("volume mask effect", () =>
        {
            if (!TotkVolumeMaskEffect.IsExported(cache))
                TotkVolumeMaskEffect.Export(romfs.Base, cache);
        });
        Step("terrain shaders", () => Export(romfs, TotkShaderExports.Terrain, cache));
        Step("cave shaders", () => Export(romfs, TotkShaderExports.Cave, cache));
        Step("depths shaders", () => Export(romfs, TotkShaderExports.Depths, cache));

        // The renderer cannot start without the HDR compose shader; the other steps only cost features.
        if (failures.Count > 0 && !cache.HasSystemAssets)
            throw new InvalidOperationException("The system assets could not be built: " + string.Join("; ", failures));
    }

    static void Once(string marker, string? directory, Action extract)
    {
        if (File.Exists(marker))
            return;
        if (directory is not null)
            Directory.CreateDirectory(directory);
        extract();
    }

    static void Export(TotkRomfs romfs, TotkShaderExports.ShadingModelExport export, CacheLayout cache)
    {
        if (!TotkShaderExports.IsExported(export, cache.Shaders))
            TotkShaderExports.Export(romfs.Base, export, cache.Shaders);
    }

    static void Run(List<string> failures, Action<string>? log, string name, Action step)
    {
        try
        {
            step();
        }
        catch (Exception ex)
        {
            failures.Add($"'{name}': {ex.Message}");
            log?.Invoke($"[prepare] system asset step '{name}' failed: {ex.Message}");
            Log.Error($"[ModelPreparer] system asset step '{name}' failed: {ex}");
        }
    }
}
