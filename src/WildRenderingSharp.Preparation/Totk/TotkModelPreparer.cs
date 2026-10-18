using ShaderLibrary.CompileTool;
using WildRenderingSharp.Rom;
using WildRenderingSharp.Storage;

namespace WildRenderingSharp.Preparation.Totk;

/// <summary>
/// Prepares one TotK actor or model into the cache: geometry, textures and animations, then material blocks, then the manifest and
/// programs, stamping the ROM files it read.
/// </summary>
public static class TotkModelPreparer
{
    // The exporters hand each binary file to the cache already packed.
    static TotkModelPreparer() => ShaderLibrary.CompileTool.ExportOutput.WriteBytes = CacheFiles.WriteBytes;

    public static string ResolveModelName(TotkRomfs romfs, string actorOrModelName) =>
        TotkActorInfo.Resolve(romfs.Layered, actorOrModelName)?.ModelName ?? actorOrModelName;

    public static string Prepare(TotkRomfs romfs, string actorOrModelName, CacheLayout cache, Action<string>? log, bool importAnims,
        TotkSourceStamp.Stages stages = TotkSourceStamp.Stages.All)
    {
        // Before recording starts: the shader archives come from the bare ROM and say nothing about which mods are active.
        string materialBfsha = TotkShaderArchives.Extract(romfs.Base, cache, "material");
        using var sources = romfs.Layered.Record();
        string modelName = Export(romfs, materialBfsha, actorOrModelName, cache, log, importAnims, stages);
        TotkSourceStamp.Write(cache.ModelDirectory(modelName), romfs, sources.Files, log, keepEarlier: !stages.HasFlag(TotkSourceStamp.Stages.Geometry) || !stages.HasFlag(TotkSourceStamp.Stages.Material),
            animations: stages.HasFlag(TotkSourceStamp.Stages.Geometry) ? importAnims : null);
        return modelName;
    }

    static string Export(TotkRomfs romfs, string materialBfsha, string actorOrModelName, CacheLayout cache, Action<string>? log, bool importAnims,
        TotkSourceStamp.Stages stages)
    {
        var rom = romfs.Layered;
        var actor = TotkActorInfo.Resolve(rom, actorOrModelName);
        string modelName = actor?.ModelName ?? actorOrModelName;
        if (actor != null)
            log?.Invoke($"[prepare] resolved actor '{actorOrModelName}' -> model '{modelName}', anim archives: {(actor.AnimPackNames.Count == 0 ? "(none named)" : string.Join(", ", actor.AnimPackNames))}");
        var assets = new TotkAssets(rom);
        byte[] fres = assets.ReadModel(modelName) ?? throw new FileNotFoundException(TotkModelFiles.Explain(rom, modelName));

        string dataDirectory = cache.ModelDirectory(modelName);
        Directory.CreateDirectory(dataDirectory);
        Directory.CreateDirectory(cache.Shaders);

        // Both are written last, so removing them first keeps a preparation that was cut short from counting as finished.
        File.Delete(cache.ManifestPath(modelName));
        // A part redone alone leaves the stamp of the others in place; one that is cut short with the stamp gone counts as not prepared at all.
        if (stages.HasFlag(TotkSourceStamp.Stages.Geometry) || stages.HasFlag(TotkSourceStamp.Stages.Material))
            TotkSourceStamp.Delete(dataDirectory);

        if (stages.HasFlag(TotkSourceStamp.Stages.Geometry))
        {
            log?.Invoke($"[prepare 1/3] geometry + textures + skeleton/anims for {modelName} -> {dataDirectory}");
            ExportTestBench.ExportModel(assets, modelName, dataDirectory, importAnims ? AnimArchives(rom, actor, modelName) : []);
            TotkPhysicsAssets.Extract(rom, actorOrModelName, modelName, dataDirectory, log);
        }

        if (stages.HasFlag(TotkSourceStamp.Stages.Material))
        {
            log?.Invoke($"[prepare 2/3] gsys_material UBOs for {modelName}");
            BuildMaterialUbo.Run(materialBfsha, fres, Path.Combine(dataDirectory, "matubo"));
        }

        log?.Invoke($"[prepare 3/3] manifest + shader decompile for {modelName}");
        ExportManifest.Run(assets, materialBfsha, modelName, dataDirectory, cache.Shaders);

        // Compressed and its textures shared, once everything it is made of is written; a reader takes either form while this runs.
        CacheFiles.Pack(dataDirectory);

        log?.Invoke($"[prepare] done - {modelName} is ready to load.");
        return modelName;
    }

    // The pack lists only the animation packs, so the model's own archive (a horse's coat variants, say) is added when it is not named.
    static List<string>? AnimArchives(IRomAccess rom, TotkActorInfo.Resolved? actor, string modelName)
    {
        if (actor is null)
            return null;
        var packs = actor.AnimPackNames.ToList();
        string project = modelName.Split('.')[0];
        if (!packs.Contains(project, StringComparer.Ordinal) && rom.Exists($"Model/{project}.anim.bfres.zs"))
            packs.Add(project);
        return packs;
    }
}
