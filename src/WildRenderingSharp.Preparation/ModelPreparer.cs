using ShaderLibrary.CompileTool;
using WildRenderingSharp.Hosting;

namespace WildRenderingSharp.Preparation;

/// <summary>
/// The <c>--prepare</c> pipeline called in-process: the same three steps in the same order as
/// <c>ShaderLibrary.CompileTool.Program</c>'s <c>--prepare</c> branch, plus the shared system assets every model needs.
/// </summary>
public static class ModelPreparer
{
    static readonly object PatchGate = new();
    static bool _patched;

    static ModelPreparer()
    {
        // ShaderLibrary shells out to MeshCodec's CLI to unpack .bfres.mc and finds it relative to its own source file, which only exists where it was compiled; the copy shipped beside this project travels with a build.
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MESHCODEC_CLI"))
            && Path.GetDirectoryName(typeof(ModelPreparer).Assembly.Location) is { Length: > 0 } dir
            && File.Exists(Path.Combine(dir, "meshcodec_cli.exe")))
        {
            Environment.SetEnvironmentVariable("MESHCODEC_CLI", Path.Combine(dir, "meshcodec_cli.exe"));
        }
    }

    public static void EnsureBfresReady(string romfsRoot)
    {
        ExternalBinaryStringTable.RomfsRoot = romfsRoot ?? "";
        lock (PatchGate)
        {
            if (_patched)
                return;
            BfresLibraryPatches.EnsureApplied();
            _patched = true;
        }
    }

    public static void SetModRomfsLayers(IEnumerable<string> romfsLayers) => RomfsOverlay.SetModRoots(romfsLayers);

    public static IReadOnlyList<string> ModRomfsLayers => RomfsOverlay.ModRoots;

    public static void EnsureSystemAssets(string romfsRoot, CacheLayout cache, Action<string>? log = null)
    {
        if (string.IsNullOrEmpty(romfsRoot) || !Directory.Exists(romfsRoot))
        {
            log?.Invoke($"[prepare] no romfs at '{romfsRoot}' - system assets not built.");
            return;
        }

        EnsureBfresReady(romfsRoot);
        Step(log, "system shaders", () => EnsureSystemShaders(romfsRoot, cache.Shaders));
        Step(log, "deferred materials", () => EnsureSystemDeferredMaterials(romfsRoot, cache.DeferredMaterials, cache.Shaders));
        Step(log, "system textures", () => EnsureSystemTextures(romfsRoot, cache.SystemTextures));
        Step(log, "cloud textures", () => EnsureCloudTextures(romfsRoot, cache.SystemTextures));
        Step(log, "sky bodies", () => EnsureSkyBodyTextures(romfsRoot, cache.SystemTextures));
        Step(log, "lens flare", () => EnsureLensFlareShaders(romfsRoot, cache.Shaders));
        Step(log, "sky LUT", () => EnsureSkyBinData(romfsRoot, cache.SkyData));
        Step(log, "cloud shader", () => EnsureCloudShader(romfsRoot, cache.Shaders));
        Step(log, "cloud noise shader", () => EnsureCloudNoiseShader(romfsRoot, cache.Shaders));
        Step(log, "sky shaders", () => EnsureSkyShaders(romfsRoot, cache.Shaders));
        Step(log, "terrain water", () =>
        {
            if (!ShaderLibrary.CompileTool.ExportTerrainWater.IsExported(cache.Shaders))
                ShaderLibrary.CompileTool.ExportTerrainWater.Run(romfsRoot, cache.Shaders);
        });
        Step(log, "terrain shaders", () =>
        {
            if (!ShaderLibrary.CompileTool.ExportTerrainShaders.IsExported(cache.Shaders))
                ShaderLibrary.CompileTool.ExportTerrainShaders.Run(romfsRoot, cache.Shaders);
        });
    }

    static void Step(Action<string>? log, string name, Action step)
    {
        try
        {
            step();
        }
        catch (Exception ex)
        {
            log?.Invoke($"[prepare] system asset step '{name}' failed: {ex.Message}");
            Console.WriteLine($"[ModelPreparer] system asset step '{name}' failed: {ex}");
        }
    }

    public static void EnsureSystemShaders(string romfsRoot, string decompiledDirectory)
    {
        if (File.Exists(Path.Combine(decompiledDirectory, "agl_hdr_compose.vert")))
            return;
        if (string.IsNullOrEmpty(romfsRoot) || !Directory.Exists(romfsRoot))
            return;

        Directory.CreateDirectory(decompiledDirectory);
        TestAglShader.ExtractHdrCompose(romfsRoot, decompiledDirectory);
    }

    public static void EnsureSystemDeferredMaterials(string romfsRoot, string deferredMaterialsDirectory, string decompiledDirectory)
    {
        bool needsMaterials = !File.Exists(Path.Combine(deferredMaterialsDirectory, "chara_skin.gsys_material.bin"));
        bool needsShaders = !Directory.Exists(decompiledDirectory) ||
            !Directory.EnumerateFiles(decompiledDirectory, "deferred_chara_skin_prog*_extracted.frag").Any();
        if (!needsMaterials && !needsShaders)
            return;
        if (string.IsNullOrEmpty(romfsRoot) || !Directory.Exists(romfsRoot))
            return;

        EnsureBfresReady(romfsRoot);

        if (needsMaterials)
            BuildMaterialUbo.RunSystemDeferred(romfsRoot, deferredMaterialsDirectory);

        if (needsShaders)
        {
            Directory.CreateDirectory(decompiledDirectory);
            string systemBfsha = RomfsPaths.ResolveMaybeCompressed(Path.Combine(romfsRoot, "Shader", "system.Product.110.product.Nin_NX_NVN.bfsha"));
            string deferredBfres = Path.Combine(romfsRoot, "Model", "SystemModel.DeferredMain.bfres.mc");
            if (!File.Exists(deferredBfres))
                deferredBfres = Path.Combine(romfsRoot, "Model", "SystemModel.DeferredMain.bfres");
            TestSystemShading.Run(systemBfsha, deferredBfres, decompiledDirectory);
        }
    }

    public static void EnsureSystemTextures(string romfsRoot, string systemTexturesDirectory)
    {
        if (File.Exists(Path.Combine(systemTexturesDirectory, "Proc3DNoise.r8")))
            return;
        if (string.IsNullOrEmpty(romfsRoot) || !Directory.Exists(romfsRoot))
            return;
        SystemTextures.ExtractProc3DNoise(romfsRoot, systemTexturesDirectory);
    }

    public static void EnsureCloudTextures(string romfsRoot, string systemTexturesDirectory)
    {
        if (File.Exists(Path.Combine(systemTexturesDirectory, "CloudNoiseBlend.r8")))
            return;

        foreach (string shipped in ShippedCloudMaskDirectories())
        {
            if (SystemTextures.InstallCapturedCloudMasks(shipped, systemTexturesDirectory))
                return;
        }

        if (string.IsNullOrEmpty(romfsRoot) || !Directory.Exists(romfsRoot))
            return;
        Console.WriteLine("[ModelPreparer] shipped cloud masks missing - falling back to the romfs " +
            "name-guess, which is known to bind the wrong textures (see res/cloud/README.md).");
        SystemTextures.ExtractCloudTextures(romfsRoot, systemTexturesDirectory);
    }

    static IEnumerable<string> ShippedCloudMaskDirectories()
    {
        if (Path.GetDirectoryName(typeof(ModelPreparer).Assembly.Location) is { Length: > 0 } assemblyDir)
            yield return Path.Combine(assemblyDir, "res", "cloud");
        yield return Path.Combine(AppContext.BaseDirectory, "res", "cloud");
    }

    public static void EnsureSkyBinData(string romfsRoot, string skyDataDirectory)
    {
        if (File.Exists(Path.Combine(skyDataDirectory, "sky_lut.bin")))
            return;
        if (string.IsNullOrEmpty(romfsRoot) || !Directory.Exists(romfsRoot))
            return;
        SkyBinTexture.ExtractMasterField(romfsRoot, skyDataDirectory);
    }

    public static void EnsureCloudShader(string romfsRoot, string decompiledDirectory)
    {
        if (File.Exists(Path.Combine(decompiledDirectory, "agl_cloud.frag")))
            return;
        if (string.IsNullOrEmpty(romfsRoot) || !Directory.Exists(romfsRoot))
            return;

        Directory.CreateDirectory(decompiledDirectory);
        TestAglShader.ExtractCloudShader(romfsRoot, decompiledDirectory);
    }

    public static void EnsureCloudNoiseShader(string romfsRoot, string decompiledDirectory)
    {
        if (File.Exists(Path.Combine(decompiledDirectory, "agl_noise_cloud.frag")))
            return;
        if (string.IsNullOrEmpty(romfsRoot) || !Directory.Exists(romfsRoot))
            return;

        Directory.CreateDirectory(decompiledDirectory);
        TestAglShader.ExtractCloudNoiseShader(romfsRoot, decompiledDirectory);
    }

    public static void EnsureSkyBodyTextures(string romfsRoot, string systemTexturesDirectory)
    {
        if (File.Exists(Path.Combine(systemTexturesDirectory, "Moon8.rg8")))
            return;
        if (string.IsNullOrEmpty(romfsRoot) || !Directory.Exists(romfsRoot))
            return;

        Directory.CreateDirectory(systemTexturesDirectory);
        SystemTextures.ExtractSkyBodyTextures(romfsRoot, systemTexturesDirectory);
    }

    public static void EnsureLensFlareShaders(string romfsRoot, string decompiledDirectory)
    {
        if (File.Exists(Path.Combine(decompiledDirectory, "agl_flare_filter_flare.frag")))
            return;
        if (string.IsNullOrEmpty(romfsRoot) || !Directory.Exists(romfsRoot))
            return;

        Directory.CreateDirectory(decompiledDirectory);
        TestAglShader.ExtractLensFlareShaders(romfsRoot, decompiledDirectory);
    }

    public static void EnsureSkyShaders(string romfsRoot, string decompiledDirectory)
    {
        // Checks the newest file this extractor produces, so a cache built before the adhoc-fog variant existed does not report "already extracted" and never gain it.
        if (File.Exists(Path.Combine(decompiledDirectory, "agl_sky_postfx_sky_fog.frag")))
            return;
        if (string.IsNullOrEmpty(romfsRoot) || !Directory.Exists(romfsRoot))
            return;

        Directory.CreateDirectory(decompiledDirectory);
        TestAglShader.ExtractSkyPostFxShaders(romfsRoot, decompiledDirectory);
    }

    public static string ResolveModelName(string romfsRoot, string actorOrModelName) =>
        ActorInfo.Resolve(romfsRoot, actorOrModelName)?.ModelName ?? actorOrModelName;

    public static string PrepareIfNeeded(string romfsRoot, string actorOrModelName, CacheLayout cache,
        Action<string>? log = null, bool importAnims = true, bool force = false)
    {
        string modelName = ResolveModelName(romfsRoot, actorOrModelName);
        if (!force && cache.IsPrepared(modelName) && IsUpToDate(romfsRoot, cache.ModelDirectory(modelName)))
        {
            log?.Invoke($"[prepare] {modelName} is already prepared and up to date.");
            return modelName;
        }
        return Prepare(romfsRoot, actorOrModelName, cache.Root, cache.Shaders, log, importAnims);
    }

    public static void PrepareMany(string romfsRoot, IReadOnlyList<string> actorOrModelNames, CacheLayout cache, int parallelism,
        Action<string>? onBegin, Action<PrepareOutcome> onOutcome, bool importAnims = true, bool force = false,
        CancellationToken cancellationToken = default)
    {
        EnsureBfresReady(romfsRoot);
        var modelGates = new System.Collections.Concurrent.ConcurrentDictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        var options = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, parallelism), CancellationToken = cancellationToken };

        Parallel.ForEach(actorOrModelNames, options, name =>
        {
            onBegin?.Invoke(name);
            try
            {
                string model = ResolveModelName(romfsRoot, name);
                lock (modelGates.GetOrAdd(model, _ => new object()))
                    model = PrepareIfNeeded(romfsRoot, name, cache, null, importAnims, force);
                onOutcome(new PrepareOutcome(name, model, null));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                onOutcome(new PrepareOutcome(name, null, OneLine(ex.GetBaseException().Message)));
            }
        });
    }

    static string OneLine(string message) => string.Join(' ', message.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).Trim();

    public static string Prepare(string romfsRoot, string actorOrModelName, string cacheRoot, string decompiledDirectory, Action<string>? log = null, bool importAnims = true)
    {
        // Every romfs lookup goes through RomfsOverlay; recording them lets IsUpToDate notice that toggling a mod changes which file would win.
        using var sources = RomfsOverlay.Recorder.Begin();
        string modelName = PrepareCore(romfsRoot, actorOrModelName, cacheRoot, decompiledDirectory, log, importAnims);
        WriteSourceStamp(Path.Combine(cacheRoot, modelName), romfsRoot, sources.Files, log);
        return modelName;
    }

    const string SourceStampFile = "romfs_sources.json";

    // Bumped when preparation starts producing something an earlier model could be missing, so IsUpToDate sends it through
    // again. 2: the model's own <Project>.anim.bfres joins the anim archives. 3: every level of detail is exported. 4: .bntx
    // textures export (CmnTex_BakeDefault). 5: textures export their whole mip chain.
    const int PreparationVersion = 5;

    sealed record SourceStampEntry(string? Path, long Size, long MTime);
    sealed record SourceStamp(string Romfs, List<string> Mods, Dictionary<string, SourceStampEntry> Files, int Version = 0);

    static void WriteSourceStamp(string dataDirectory, string romfsRoot, IReadOnlyDictionary<string, string?> files, Action<string>? log)
    {
        var entries = new Dictionary<string, SourceStampEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var (rel, winner) in files)
        {
            var info = winner is null ? null : new FileInfo(winner);
            entries[rel] = info is { Exists: true }
                ? new SourceStampEntry(winner, info.Length, info.LastWriteTimeUtc.Ticks)
                : new SourceStampEntry(null, 0, 0);
        }

        var fromMods = entries.Where(e => e.Value.Path is { } p && RomfsOverlay.ModRoots.Any(m => p.StartsWith(m, StringComparison.OrdinalIgnoreCase))).ToList();
        foreach (var (rel, e) in fromMods)
            log?.Invoke($"[prepare] mod file: {rel} <- {e.Path}");

        var stamp = new SourceStamp(Path.GetFullPath(romfsRoot), RomfsOverlay.ModRoots.ToList(), entries, PreparationVersion);
        File.WriteAllText(Path.Combine(dataDirectory, SourceStampFile),
            System.Text.Json.JsonSerializer.Serialize(stamp, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    public static bool IsUpToDate(string romfsRoot, string dataDirectory)
    {
        string stampPath = Path.Combine(dataDirectory, SourceStampFile);
        if (!File.Exists(stampPath))
            return RomfsOverlay.ModRoots.Count == 0;

        SourceStamp? stamp;
        try { stamp = System.Text.Json.JsonSerializer.Deserialize<SourceStamp>(File.ReadAllText(stampPath)); }
        catch { return false; }
        if (stamp is null || stamp.Version != PreparationVersion)
            return false;

        foreach (var (rel, recorded) in stamp.Files)
        {
            string? now = RomfsOverlay.Peek(romfsRoot, rel);
            if (!string.Equals(now is null ? null : Path.GetFullPath(now),
                               recorded.Path is null ? null : Path.GetFullPath(recorded.Path),
                               StringComparison.OrdinalIgnoreCase))
                return false;
            if (now is null)
                continue;
            var info = new FileInfo(now);
            if (info.Length != recorded.Size || info.LastWriteTimeUtc.Ticks != recorded.MTime)
                return false;
        }
        return true;
    }

    static string PrepareCore(string romfsRoot, string actorOrModelName, string cacheRoot, string decompiledDirectory, Action<string>? log, bool importAnims)
    {
        EnsureBfresReady(romfsRoot);

        var actor = ActorInfo.Resolve(romfsRoot, actorOrModelName);
        string modelName = actor?.ModelName ?? actorOrModelName;
        if (actor != null)
            log?.Invoke($"[prepare] resolved actor '{actorOrModelName}' -> model '{modelName}', anim archives: {(actor.AnimPackNames.Count == 0 ? "(none named)" : string.Join(", ", actor.AnimPackNames))}");

        string materialBfsha = RomfsPaths.ResolveMaybeCompressed(Path.Combine(romfsRoot, "Shader", "material.Product.110.product.Nin_NX_NVN.bfsha"));
        string? modelMc = RomfsPaths.ModelFile(romfsRoot, modelName)
            ?? throw new FileNotFoundException(RomfsPaths.Explain(romfsRoot, modelName));

        string dataDirectory = Path.Combine(cacheRoot, modelName);
        Directory.CreateDirectory(dataDirectory);
        Directory.CreateDirectory(decompiledDirectory);

        // A preparation can be cut short (an out-of-process preparer is killed mid-write when the user moves on). The manifest and source stamp make a model count as prepared, and both are written last, so removing them first means an unfinished preparation is never mistaken for a finished one.
        File.Delete(Path.Combine(dataDirectory, $"{modelName}.manifest.json"));
        File.Delete(Path.Combine(dataDirectory, SourceStampFile));

        log?.Invoke($"[prepare 1/3] geometry + textures + skeleton/anims for {modelName} -> {dataDirectory}");
        ExportTestBench.ExportModel(romfsRoot, modelName, dataDirectory, importAnims ? AnimArchives(romfsRoot, actor, modelName) : []);

        ExtractPhysics(romfsRoot, actorOrModelName, modelName, dataDirectory, log);

        log?.Invoke($"[prepare 2/3] gsys_material UBOs for {modelName}");
        BuildMaterialUbo.Run(materialBfsha, modelMc, Path.Combine(dataDirectory, "matubo"));

        log?.Invoke($"[prepare 3/3] manifest + shader decompile for {modelName}");
        ExportManifest.Run(romfsRoot, materialBfsha, modelName, dataDirectory, decompiledDirectory);

        log?.Invoke($"[prepare] done - {modelName} is ready to load.");
        return modelName;
    }

    // The animation archives to export from: those the actor's pack names, plus the model's own <Project>.anim.bfres when the
    // pack does not name it. The pack lists only the animation packs, and the model's archive holds the rest (a horse's coat
    // and eye variants, a Boss Bokoblin's colour patterns), so without it those texture-pattern clips never reach the cache.
    // Null, for an actor with no pack, leaves ShaderLibrary to guess by prefix, which includes it.
    static List<string>? AnimArchives(string romfsRoot, ActorInfo.Resolved? actor, string modelName)
    {
        if (actor is null)
            return null;
        var packs = actor.AnimPackNames.ToList();
        string project = modelName.Split('.')[0];
        if (!packs.Contains(project, StringComparer.Ordinal)
            && RomfsOverlay.Exists(romfsRoot, Path.Combine("Model", $"{project}.anim.bfres.zs")))
            packs.Add(project);
        return packs;
    }

    // Copies the actor's Havok Cloth (.bphcl) and Phive Helper Bone (.bphhb) files out of its pack into the model's cache
    // directory. The renderer does not simulate them, but a host that does can read them beside the model (see
    // RenderActor.ModifyPose).
    private static void ExtractPhysics(string romfsRoot, string actorOrModelName, string modelName, string dataDirectory, Action<string>? log)
    {
        bool foundAny = false;

        // 1. Try extracting from actor's SARC pack in RomFS
        if (!string.IsNullOrEmpty(romfsRoot) && Directory.Exists(romfsRoot))
        {
            // Through the overlay, so a mod's own cloth/helper-bone data comes with its actor pack.
            string[] candidatePacks =
            [
                RomfsOverlay.Resolve(romfsRoot, "Pack", "Actor", $"{actorOrModelName}.pack.zs"),
                RomfsOverlay.Resolve(romfsRoot, "Pack", "Actor", $"{modelName}.pack.zs")
            ];

            foreach (string packPath in candidatePacks)
            {
                if (!File.Exists(packPath)) continue;

                try
                {
                    TotkCommon.Totk.Config.GamePath = romfsRoot;
                    byte[] raw = File.ReadAllBytes(packPath);
                    byte[] decompressed = TotkCommon.Zstd.IsCompressed(raw) ? TotkCommon.Totk.Zstd.Decompress(raw) : raw;
                    var sarc = SarcLibrary.Sarc.FromBinary(new ArraySegment<byte>(decompressed));
                    foreach (var kv in sarc)
                    {
                        if (kv.Key.EndsWith(".bphcl", StringComparison.OrdinalIgnoreCase) ||
                            kv.Key.EndsWith(".bphhb", StringComparison.OrdinalIgnoreCase))
                        {
                            string outPath = Path.Combine(dataDirectory, Path.GetFileName(kv.Key));
                            File.WriteAllBytes(outPath, kv.Value.ToArray());
                            log?.Invoke($"[prepare] extracted physics asset '{Path.GetFileName(kv.Key)}' -> {outPath}");
                            foundAny = true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    log?.Invoke($"[prepare] error extracting physics from '{packPath}': {ex.Message}");
                }

                if (foundAny) break;
            }
        }

        // 2. Fallback to local test_cloth assets (developer checkouts only) if the romfs had none.
        if (!foundAny)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] testSearchPaths =
            [
                Path.Combine(baseDir, "res", "test_cloth"),
                Path.Combine(baseDir, "..", "..", "..", "..", "res", "test_cloth"),
                Path.Combine(Directory.GetCurrentDirectory(), "res", "test_cloth")
            ];

            foreach (var testRoot in testSearchPaths)
            {
                if (!Directory.Exists(testRoot)) continue;
                string[] matchDirs =
                [
                    Path.Combine(testRoot, actorOrModelName),
                    Path.Combine(testRoot, modelName)
                ];
                foreach (var matchDir in matchDirs)
                {
                    if (!Directory.Exists(matchDir)) continue;
                    foreach (var file in Directory.GetFiles(matchDir, "*.*", SearchOption.AllDirectories))
                    {
                        if (file.EndsWith(".bphcl", StringComparison.OrdinalIgnoreCase) ||
                            file.EndsWith(".bphhb", StringComparison.OrdinalIgnoreCase))
                        {
                            string outPath = Path.Combine(dataDirectory, Path.GetFileName(file));
                            File.Copy(file, outPath, overwrite: true);
                            log?.Invoke($"[prepare] copied test physics asset '{Path.GetFileName(file)}' -> {outPath}");
                            foundAny = true;
                        }
                    }
                }
                if (foundAny) break;
            }
        }
    }
}
