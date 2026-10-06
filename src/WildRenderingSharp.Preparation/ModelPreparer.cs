using ShaderLibrary.CompileTool;
using WildRenderingSharp.Hosting;

namespace WildRenderingSharp.Preparation;

/// <summary>
/// The <c>--prepare</c> pipeline, called in-process instead of via
/// <c>dotnet run --project ShaderLibrary/ShaderLibrary.CompileTool -- &lt;romfs&gt; &lt;Model&gt; --prepare</c>.
/// Mirrors <c>ShaderLibrary.CompileTool.Program</c>'s own <c>--prepare</c> branch exactly (same
/// three steps, same order) - this is not a reimplementation, just calling the same static
/// methods the CLI does - and adds the shared system assets every model needs.
/// </summary>
/// <remarks>
/// Everything here is static and writes only under the <see cref="CacheLayout"/> it is given.
/// Mod layering is ShaderLibrary's process-wide <see cref="RomfsOverlay"/>; set it with
/// <see cref="SetModRomfsLayers"/>.
/// </remarks>
public static class ModelPreparer
{
    static readonly object PatchGate = new();
    static bool _patched;

    static ModelPreparer()
    {
        // ShaderLibrary shells out to MeshCodec's CLI to unpack every .bfres.mc, and finds it
        // relative to its own source file - a path that only exists where it was compiled. The
        // copy this project ships beside itself is the one that travels with a build.
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MESHCODEC_CLI"))
            && Path.GetDirectoryName(typeof(ModelPreparer).Assembly.Location) is { Length: > 0 } dir
            && File.Exists(Path.Combine(dir, "meshcodec_cli.exe")))
        {
            Environment.SetEnvironmentVariable("MESHCODEC_CLI", Path.Combine(dir, "meshcodec_cli.exe"));
        }
    }

    /// <summary>
    /// Applies ShaderLibrary's runtime patches to its vendored BfresLibrary (see
    /// <c>BfresLibraryPatches.cs</c>) and points its external string table at the romfs. Idempotent.
    /// </summary>
    /// <remarks>
    /// Has to run before ANY BFRES/BFSHA is parsed - including the system deferred materials,
    /// which are the first thing a fresh cache builds. Without the patches, that build crashes on
    /// BfresLibrary's own duplicate-RenderInfo-key bug; without the string table, a V10 material's
    /// real name cannot be resolved.
    /// </remarks>
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

    /// <summary>Layers mod romfs folders over the base romfs, highest priority first. Replacement is per file; see <see cref="RomfsOverlay"/>.</summary>
    public static void SetModRomfsLayers(IEnumerable<string> romfsLayers) => RomfsOverlay.SetModRoots(romfsLayers);

    /// <summary>The mod romfs layers currently in effect, highest priority first.</summary>
    public static IReadOnlyList<string> ModRomfsLayers => RomfsOverlay.ModRoots;

    /// <summary>
    /// Every shared asset the live pipeline needs, built once into <paramref name="cache"/> -
    /// each step is skipped if its output is already there, so this is cheap on every later run.
    /// </summary>
    /// <remarks>
    /// <c>agl_hdr_compose</c> is the hard requirement: <c>DeferredPipeline</c> links it in its
    /// constructor. Everything else degrades - a missing deferred material renders against zeros,
    /// a missing cloud shader just leaves the cloud pass unavailable - but every one of them is
    /// real data that makes the frame match the game, so all of them are built.
    /// </remarks>
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
    }

    /// <summary>
    /// Runs one system-asset step, isolating its failure. Every step is independent; one broken
    /// extraction (a texture in a format nothing decodes yet, say) must not cost the host every
    /// asset after it.
    /// </summary>
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

    /// <summary>
    /// A handful of SYSTEM shaders (<c>agl_hdr_compose</c> - the final tonemap step every model
    /// needs regardless of which one is loaded) aren't produced by <see cref="Prepare"/> at all;
    /// they come from agl's own shader archives
    /// (<c>Shader/ApplicationPackage.Nin_NX_NVN.release.sarc.zs</c> -&gt; <c>AglShader.sharcb</c>),
    /// a completely different container/decompile path than every material shader
    /// <see cref="Prepare"/> handles - see <see cref="TestAglShader.ExtractHdrCompose"/>, which
    /// does the real extraction straight from romfs (no pre-decompiled copy checked into the repo -
    /// decompiled game shader output can't be committed to source control). <c>DeferredPipeline</c>
    /// loads <c>agl_hdr_compose</c> unconditionally at construction, so a shader directory missing
    /// it doesn't just fail to render - it throws out of the pipeline's constructor.
    /// </summary>
    public static void EnsureSystemShaders(string romfsRoot, string decompiledDirectory)
    {
        if (File.Exists(Path.Combine(decompiledDirectory, "agl_hdr_compose.vert")))
            return;
        if (string.IsNullOrEmpty(romfsRoot) || !Directory.Exists(romfsRoot))
            return;

        Directory.CreateDirectory(decompiledDirectory);
        TestAglShader.ExtractHdrCompose(romfsRoot, decompiledDirectory);
    }

    /// <summary>
    /// Builds the shared deferred-resolve gsys_material blocks once, if missing - see
    /// <c>BuildMaterialUbo.RunSystemDeferred</c>'s remarks for why this step existed nowhere in
    /// the pipeline before: <c>DeferredResolvePass</c> was silently running every deferred-resolve
    /// pass (chara_skin, chara_hair, chara_eye, chara_grossy, chara_nonmetal, chara_metal) against
    /// an all-zero material block, because nothing ever wrote
    /// <c>&lt;dataDirectory&gt;/matubo_deferred/</c>. Unlike that old per-model path, this is one
    /// shared archive/model - built once regardless of which creature gets loaded, exactly like
    /// <see cref="EnsureSystemShaders"/> shares the tonemap shaders.
    ///
    /// ALSO ensures the deferred-resolve passes' own compiled shader PROGRAMS exist in
    /// <paramref name="decompiledDirectory"/> - a second, independent gap discovered on a fresh
    /// clone: <c>DeferredResolvePass.ResolveDeferredPasses</c> looks for
    /// <c>deferred_&lt;pass&gt;_prog*_extracted.frag/vert</c> and silently skips a pass entirely if
    /// missing ("[skip] no extracted deferred shader for pass..."), which is a much quieter failure
    /// than the material-UBO gap above (that one at least renders with an all-zero block; this one
    /// renders NOTHING for the pass at all). These come from a completely different decompile path
    /// than every material shader <see cref="Prepare"/> handles - <c>TestSystemShading</c> resolves
    /// each deferred pass's real program index against <c>system.bfsha</c> +
    /// <c>SystemModel.DeferredMain.bfres.mc</c> and decompiles it. Gated on its own existence check
    /// since the two caches (materials vs. shader programs) can independently be present or missing.
    /// </summary>
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

    /// <summary>
    /// Extracts real static assets behind "system" texture names once, if missing - see
    /// <c>SystemTextures.ExtractProc3DNoise</c>'s remarks. Currently just <c>cTex_Proc3DNoise</c>
    /// (a real 3D Worley/Perlin noise volume, <c>TexToGo/3DWorleyPerlinNoise_Fi.bntx.zs</c>) -
    /// every other "system" sampler the forward/deferred-resolve shaders reference is a dynamic,
    /// camera/world-state-dependent render target (shadow cascades, sky scattering, the Depths'
    /// darkness maps, terrain streaming) the renderer has no equivalent subsystem for at all, so
    /// there is no romfs file to extract for those.
    /// </summary>
    public static void EnsureSystemTextures(string romfsRoot, string systemTexturesDirectory)
    {
        if (File.Exists(Path.Combine(systemTexturesDirectory, "Proc3DNoise.r8")))
            return;
        if (string.IsNullOrEmpty(romfsRoot) || !Directory.Exists(romfsRoot))
            return;
        SystemTextures.ExtractProc3DNoise(romfsRoot, systemTexturesDirectory);
    }

    /// <summary>
    /// Installs the cloud masks, preferring the shipped captured ones over a romfs guess.
    /// </summary>
    /// <remarks>
    /// The romfs path stays as a fallback rather than being deleted, but it is known to be WRONG -
    /// it picks textures by name (<c>PolarSphereMappingNoise_Fi</c>/<c>VolumeMist03</c>) and both
    /// were disproved against a capture of the game's real cloud draw (correlation ~0.01). It only
    /// runs if the shipped files are missing, so that a build without them degrades to the old
    /// behaviour instead of to no clouds at all. See <c>res/cloud/README.md</c>.
    ///
    /// Guarded on <c>CloudNoiseBlend.r8</c>, not <c>CloudBase.r8</c>: the old two-texture extraction
    /// wrote the latter, so checking it would leave a stale, wrong cache in place forever.
    /// </remarks>
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

    /// <summary>
    /// Where the captured masks can be: beside this assembly (the preparer's own output, which is
    /// where its project copies them), then beside the host executable (the same files flow on to
    /// a host that references this project).
    /// </summary>
    static IEnumerable<string> ShippedCloudMaskDirectories()
    {
        if (Path.GetDirectoryName(typeof(ModelPreparer).Assembly.Location) is { Length: > 0 } assemblyDir)
            yield return Path.Combine(assemblyDir, "res", "cloud");
        yield return Path.Combine(AppContext.BaseDirectory, "res", "cloud");
    }

    /// <summary>Extracts the real sky-scattering LUT once, if missing - see <see cref="SkyBinTexture"/>'s own remarks for what it is and how it was found.</summary>
    public static void EnsureSkyBinData(string romfsRoot, string skyDataDirectory)
    {
        if (File.Exists(Path.Combine(skyDataDirectory, "sky_lut.bin")))
            return;
        if (string.IsNullOrEmpty(romfsRoot) || !Directory.Exists(romfsRoot))
            return;
        SkyBinTexture.ExtractMasterField(romfsRoot, skyDataDirectory);
    }

    /// <summary>
    /// Extracts the real cloud billboard shader (<c>agl::fx::Cloud</c>'s own <c>cloud</c> program,
    /// from <c>Lib/agl/agl_resource.Nin_NX_NVN.release.sarc.zs</c> -&gt; <c>agl_technique.sharcb</c>)
    /// once, if missing - see <see cref="TestAglShader.ExtractCloudShader"/> for the archive location
    /// and the reasoning behind the one macro combination it decompiles. One shared shader regardless
    /// of which model is loaded, same reasoning as <see cref="EnsureSystemShaders"/>.
    /// </summary>
    public static void EnsureCloudShader(string romfsRoot, string decompiledDirectory)
    {
        if (File.Exists(Path.Combine(decompiledDirectory, "agl_cloud.frag")))
            return;
        if (string.IsNullOrEmpty(romfsRoot) || !Directory.Exists(romfsRoot))
            return;

        Directory.CreateDirectory(decompiledDirectory);
        TestAglShader.ExtractCloudShader(romfsRoot, decompiledDirectory);
    }

    /// <summary>Extracts the real procedural noise generator that bakes the "cloud_noise" texture <c>agl_cloud</c> reads once, if missing - see <see cref="TestAglShader.ExtractCloudNoiseShader"/>.</summary>
    public static void EnsureCloudNoiseShader(string romfsRoot, string decompiledDirectory)
    {
        if (File.Exists(Path.Combine(decompiledDirectory, "agl_noise_cloud.frag")))
            return;
        if (string.IsNullOrEmpty(romfsRoot) || !Directory.Exists(romfsRoot))
            return;

        Directory.CreateDirectory(decompiledDirectory);
        TestAglShader.ExtractCloudNoiseShader(romfsRoot, decompiledDirectory);
    }

    /// <summary>Extracts the sun disc and the eight moon-phase sprites once into the shared system-texture cache - see <c>SkyBodyPass</c> for what they are and why they are sprites.</summary>
    public static void EnsureSkyBodyTextures(string romfsRoot, string systemTexturesDirectory)
    {
        if (File.Exists(Path.Combine(systemTexturesDirectory, "Moon8.rg8")))
            return;
        if (string.IsNullOrEmpty(romfsRoot) || !Directory.Exists(romfsRoot))
            return;

        Directory.CreateDirectory(systemTexturesDirectory);
        SystemTextures.ExtractSkyBodyTextures(romfsRoot, systemTexturesDirectory);
    }

    /// <summary>Extracts the real lens-flare program once - see <c>LensFlarePass</c>.</summary>
    public static void EnsureLensFlareShaders(string romfsRoot, string decompiledDirectory)
    {
        if (File.Exists(Path.Combine(decompiledDirectory, "agl_flare_filter_flare.frag")))
            return;
        if (string.IsNullOrEmpty(romfsRoot) || !Directory.Exists(romfsRoot))
            return;

        Directory.CreateDirectory(decompiledDirectory);
        TestAglShader.ExtractLensFlareShaders(romfsRoot, decompiledDirectory);
    }

    /// <summary>
    /// Extracts the real <c>agl::pfx::Sky</c> programs (per-frame postfx plus the whole Bruneton
    /// precompute chain), if missing - see <see cref="TestAglShader.ExtractSkyPostFxShaders"/>.
    /// </summary>
    public static void EnsureSkyShaders(string romfsRoot, string decompiledDirectory)
    {
        // Checks the NEWEST file this extractor produces, not the oldest. Guarding on
        // agl_sky_postfx_sky.frag alone meant every cache built before the adhoc-fog variant
        // existed reported "already extracted" and silently never gained it - the horizon fog
        // band would then just never appear, with nothing to explain why.
        if (File.Exists(Path.Combine(decompiledDirectory, "agl_sky_postfx_sky_fog.frag")))
            return;
        if (string.IsNullOrEmpty(romfsRoot) || !Directory.Exists(romfsRoot))
            return;

        Directory.CreateDirectory(decompiledDirectory);
        TestAglShader.ExtractSkyPostFxShaders(romfsRoot, decompiledDirectory);
    }

    /// <summary>
    /// The model an actor name resolves to - what the cache directory and every exported file are
    /// named after - without preparing anything. A name with no actor pack resolves to itself.
    /// </summary>
    public static string ResolveModelName(string romfsRoot, string actorOrModelName) =>
        ActorInfo.Resolve(romfsRoot, actorOrModelName)?.ModelName ?? actorOrModelName;

    /// <summary>
    /// Prepares <paramref name="actorOrModelName"/> unless the cache already holds an up-to-date
    /// copy (see <see cref="IsUpToDate"/>), returning the resolved model name either way.
    /// </summary>
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

    /// <summary>
    /// Prepares many actors at once, <paramref name="parallelism"/> models at a time, sharing one
    /// parsed shader archive (<see cref="SharedBfsha"/>) and the decompiled programs between them.
    /// The caller has already called <see cref="EnsureSystemAssets"/> and set the mod layers.
    /// </summary>
    /// <param name="onBegin">Called as each name starts - lets a supervising process work out which name was in flight if a native crash takes the process down.</param>
    /// <param name="onOutcome">Called as each name finishes, successfully or not.</param>
    /// <remarks>
    /// Names that resolve to the same model are serialised on that model, so its directory never
    /// has two writers; the second normally finds it up to date and returns at once. An exception
    /// fails only its own name.
    /// </remarks>
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

    /// <param name="cacheRoot">The cache root (e.g. <see cref="CacheLayout.Root"/>) - the resolved model's own files land in <c>&lt;cacheRoot&gt;/&lt;resolvedModelName&gt;/</c>, never directly in this directory.</param>
    /// <param name="actorOrModelName">
    /// Either a real ACTOR name (e.g. "Enemy_Dragon_Darkness" - preferred: resolved via
    /// <see cref="ActorInfo.Resolve"/> against <c>Pack/Actor/&lt;name&gt;.pack.zs</c>, which
    /// authoritatively names both the real model file and its real skeletal-anim archives, no
    /// guessing at all) or a bare model name/stem for something with no actor pack at all (props,
    /// some creatures) - falls back to the old pack-prefix-guessing path for both the model file
    /// (<see cref="RomfsPaths.ModelFile"/>) and its anim archives
    /// (<see cref="ExportTestBench.ExportExternalAnims"/>).
    /// </param>
    /// <returns>The real, resolved model name used for every exported file and the cache subdirectory - may differ from <paramref name="actorOrModelName"/> when actor resolution found a different underlying model (e.g. "Enemy_Chuchu_Junior" -&gt; "Enemy_Chuchu_Junior.Chuchu_Plain_Junior").</returns>
    public static string Prepare(string romfsRoot, string actorOrModelName, string cacheRoot, string decompiledDirectory, Action<string>? log = null, bool importAnims = true)
    {
        // Every romfs lookup below goes through RomfsOverlay; recording them is what lets
        // IsUpToDate notice later that enabling/disabling a mod changes which file would win.
        using var sources = RomfsOverlay.Recorder.Begin();
        string modelName = PrepareCore(romfsRoot, actorOrModelName, cacheRoot, decompiledDirectory, log, importAnims);
        WriteSourceStamp(Path.Combine(cacheRoot, modelName), romfsRoot, sources.Files, log);
        return modelName;
    }

    const string SourceStampFile = "romfs_sources.json";

    /// <summary>
    /// Bumped whenever preparation starts producing something a model prepared before could be
    /// missing, so <see cref="IsUpToDate"/> sends those models through again. 2: the model's own
    /// <c>&lt;Project&gt;.anim.bfres</c> joins the archives animations are exported from. 3: every level of detail
    /// is exported (the index file grows, the manifest gains "lods").
    /// </summary>
    const int PreparationVersion = 3;

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

    /// <summary>
    /// False if the prepared model in <paramref name="dataDirectory"/> was built from different
    /// romfs files than the current romfs root + mod set would supply now - a mod enabled or
    /// disabled that touches one of its files, or a mod's file edited on disk - and so needs
    /// re-preparing. Only files the prepare actually looked up are checked, so toggling an
    /// unrelated mod costs nothing.
    /// </summary>
    /// <remarks>A cache predating this check has no stamp; it is trusted only while no mods are active, since nothing records what it was built from. A stamp from an older <see cref="PreparationVersion"/> is never up to date.</remarks>
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

        // A preparation can be cut short - a host cancels it when the user moves on to another
        // actor, which for an out-of-process preparer means the process is killed mid-write. The
        // manifest and the source stamp are what make a model count as prepared and up to date,
        // and both are written last, so removing them first means a preparation that never
        // finished can never be mistaken for one that did.
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

    /// <summary>
    /// The animation archives to export from: the ones the actor's pack names, plus the model's own
    /// <c>&lt;Project&gt;.anim.bfres</c> when it exists and the pack does not name it. The pack lists
    /// only the animation packs, and the model's own archive is where some actors keep the rest -
    /// a horse's coat and eye variants, a Boss Bokoblin's colour patterns - so without it those
    /// texture-pattern clips (and the alternate textures they select) never reach the cache. Prism
    /// reads the same set. Null - for an actor with no pack - leaves ShaderLibrary to guess by the
    /// model's prefix, which already includes that archive.
    /// </summary>
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

    /// <summary>
    /// Copies the actor's Havok Cloth (<c>.bphcl</c>) and Phive Helper Bone (<c>.bphhb</c>) files
    /// out of its pack into the model's cache directory. The renderer does not simulate them - cloth
    /// and helper bones are their own libraries (HkxSimSharp, HkxHbSharp) - but a host that does can
    /// read them from beside the model rather than opening the pack itself; see
    /// <c>RenderActor.ModifyPose</c> for where their result goes.
    /// </summary>
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
