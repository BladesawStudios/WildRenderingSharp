using ShaderLibrary.CompileTool;
using WildRenderingSharp.Logging;
using WildRenderingSharp.Profiles.Totk.Atmosphere.Palettes;
using WildRenderingSharp.Storage;

namespace WildRenderingSharp.Preparation.Totk;

/// <summary>Builds the assets every TotK model shares, once per cache, from the bare ROM. A failing step costs a feature, not the renderer, so only the HDR compose shader is required.</summary>
public static class TotkSystemAssets
{
    const string DeferredModel = "SystemModel.DeferredMain";

    public static void Ensure(TotkRomfs romfs, CacheLayout cache, Action<string>? log)
    {
        var failures = new List<string>();
        void Step(string name, Action step) => Run(failures, log, name, step);
        Step("system shaders", () => Once(Path.Combine(cache.Shaders, "agl_hdr_compose.vert"), cache.Shaders, () => TotkAglShaders.HdrCompose(romfs.Base, cache.Shaders)));
        Step("deferred materials", () => DeferredPasses(romfs, cache));
        Step("system textures", () => Once(Path.Combine(cache.SystemTextures, "Proc3DNoise.r8"), null, () => TotkSystemTextures.ExtractProc3DNoise(romfs.Base, cache.SystemTextures)));
        Step("cloud textures", () =>
        {
            if (!TotkCloudMasks.IsInstalled(cache.SystemTextures))
                TotkCloudMasks.Install(romfs.Base, cache.SystemTextures);
        });
        Step("sky bodies", () => Once(Path.Combine(cache.SystemTextures, "Moon8.rg8"), cache.SystemTextures, () => TotkSystemTextures.ExtractSkyBodies(romfs.Base, cache.SystemTextures)));
        Step("lens flare", () => Once(Path.Combine(cache.Shaders, "agl_flare_filter_flare.frag"), cache.Shaders, () => TotkAglShaders.LensFlare(romfs.Base, cache.Shaders)));
        Step("cloud shader", () => Once(Path.Combine(cache.Shaders, "agl_cloud.frag"), cache.Shaders, () => TotkAglShaders.Cloud(romfs.Base, cache.Shaders)));
        // Checks the newest file this extractor produces, so a cache built before the adhoc-fog variant existed gains it.
        Step("sky shaders", () => Once(Path.Combine(cache.Shaders, "agl_sky_postfx_sky_fog.frag"), cache.Shaders, () => TotkAglShaders.SkyPostFx(romfs.Base, cache.Shaders)));
        Step("terrain water", () =>
        {
            if (!TotkTerrainWater.IsExported(cache.Shaders))
                TotkTerrainWater.Export(romfs.Base, cache.Shaders);
        });
        Step("terrain shaders", () => Export(romfs, TotkShaderExports.Terrain, cache));
        Step("cave shaders", () => Export(romfs, TotkShaderExports.Cave, cache));

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

    // The G-buffer resolve passes: each pass's material block, then the programs, named for the pass they implement.
    static void DeferredPasses(TotkRomfs romfs, CacheLayout cache)
    {
        bool needsMaterials = !File.Exists(Path.Combine(cache.DeferredMaterials, "chara_skin.gsys_material.bin"));
        bool needsShaders = !Directory.Exists(cache.Shaders)
            || !Directory.EnumerateFiles(cache.Shaders, "deferred_chara_skin_prog*_extracted.frag").Any();
        if (!needsMaterials && !needsShaders)
            return;

        string systemBfsha = TotkShaderArchives.Extract(romfs.Base, cache, "system");
        byte[] model = ReadDeferredModel(romfs);
        if (needsMaterials)
            BuildMaterialUbo.Run(systemBfsha, model, cache.DeferredMaterials);
        if (needsShaders)
        {
            Directory.CreateDirectory(cache.Shaders);
            string modelPath = Path.Combine(TotkShaderArchives.Folder(cache), DeferredModel + ".bfres");
            AtomicFile.WriteAllBytes(modelPath, model);
            TestSystemShading.Run(systemBfsha, modelPath, cache.Shaders);
        }
    }

    static byte[] ReadDeferredModel(TotkRomfs romfs)
    {
        if (new TotkAssets(romfs.Base).ReadModel(DeferredModel) is { } compressed)
            return compressed;
        return romfs.Base.Exists($"Model/{DeferredModel}.bfres")
            ? romfs.Base.ReadAllBytesNested($"Model/{DeferredModel}.bfres").ToArray()
            : throw new FileNotFoundException($"Model/{DeferredModel}.bfres.mc");
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
