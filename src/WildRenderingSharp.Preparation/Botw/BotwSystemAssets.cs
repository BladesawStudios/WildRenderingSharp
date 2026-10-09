using ShaderLibrary.CompileTool;
using WildRenderingSharp.Rom;
using WildRenderingSharp.Hosting;
using WildRenderingSharp.Preparation.Totk;

namespace WildRenderingSharp.Preparation.Botw;

/// <summary>The shared half of BotW's frame: the deferred shading passes the lighting is made of, decompiled, with each pass's material block.</summary>
public static class BotwSystemAssets
{
    const string ShadingModel = "uking_sys_shading";

    public static bool IsBuilt(CacheLayout cache) =>
        File.Exists(Path.Combine(cache.DeferredMaterials, "chara_skin.gsys_material.bin"))
        && Directory.Exists(cache.Shaders)
        && Directory.EnumerateFiles(cache.Shaders, "deferred_chara_skin_prog*_extracted.frag").Any();

    public static void Ensure(IRomAccess rom, CacheLayout cache, Action<string>? log = null)
    {
        if (IsBuilt(cache))
            return;

        BfresPatches.EnsureApplied();
        log?.Invoke("[prepare] BotW deferred shading passes");
        var assets = new BotwAssets(rom);
        string archives = Path.Combine(cache.Root, "_botw_shader_archives");
        string systemBfsha = assets.ExtractShaderArchive("uking_sys", archives);

        string modelPath = Path.Combine(archives, "SystemModel.bfres");
        byte[] model = rom.ReadAllBytesNested("Pack/Bootup_Graphics.pack//Model/SystemModel.sbfres").ToArray();
        File.WriteAllBytes(modelPath, model);

        Directory.CreateDirectory(cache.Shaders);
        Directory.CreateDirectory(cache.DeferredMaterials);
        TestSystemShading.Run(systemBfsha, modelPath, cache.Shaders, ShadingModel);
        BuildMaterialUbo.Run(systemBfsha, model, cache.DeferredMaterials);
    }
}
