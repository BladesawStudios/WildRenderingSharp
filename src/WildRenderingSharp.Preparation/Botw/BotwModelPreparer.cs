using ShaderLibrary.CompileTool;
using WildRenderingSharp.Hosting;

namespace WildRenderingSharp.Preparation.Botw;

/// <summary>Prepares a Breath of the Wild model for the renderer, reading the Switch dump's packs through <see cref="BotwAssets"/>.</summary>
public static class BotwModelPreparer
{
    const string MaterialArchive = "uking_mat";

    public static string PrepareIfNeeded(string romRoot, string modelName, CacheLayout cache, Action<string>? log = null, bool force = false)
    {
        if (!force && cache.IsPrepared(modelName))
        {
            log?.Invoke($"[prepare] {modelName} is already prepared.");
            return modelName;
        }
        return Prepare(romRoot, modelName, cache, log);
    }

    public static string Prepare(string romRoot, string modelName, CacheLayout cache, Action<string>? log = null)
    {
        ModelPreparer.EnsureBfresReady(romRoot);
        var assets = new BotwAssets(romRoot);

        string dataDirectory = cache.ModelDirectory(modelName);
        Directory.CreateDirectory(dataDirectory);
        Directory.CreateDirectory(cache.Shaders);

        // The manifest is written last, so removing it first keeps a cut-short preparation from counting as finished.
        File.Delete(Path.Combine(dataDirectory, $"{modelName}.manifest.json"));

        string materialBfsha = assets.ExtractShaderArchive(MaterialArchive, Path.Combine(cache.Root, "_botw_shader_archives"));
        byte[] fres = assets.ReadModel(modelName) ?? throw new FileNotFoundException($"BotW has no model '{modelName}'.");

        log?.Invoke($"[prepare 1/3] geometry + textures + skeleton for {modelName} -> {dataDirectory}");
        ExportTestBench.ExportModel(assets, modelName, dataDirectory);

        log?.Invoke($"[prepare 2/3] gsys_material UBOs for {modelName}");
        BuildMaterialUbo.Run(materialBfsha, fres, Path.Combine(dataDirectory, "matubo"));

        log?.Invoke($"[prepare 3/3] manifest + shader decompile for {modelName}");
        ExportManifest.Run(assets, materialBfsha, modelName, dataDirectory, cache.Shaders);

        log?.Invoke($"[prepare] done - {modelName} is ready to load.");
        return modelName;
    }
}
