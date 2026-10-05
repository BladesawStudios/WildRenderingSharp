# WildRenderingSharp

WildRenderingSharp reimplements part of *The Legend of Zelda: Tears of the Kingdom*'s rendering
pipeline to load the game's real models and drive them with the game's **actual** shaders, as a
library any tool can host. It is not a from-scratch renderer with hand-written shaders standing in
for the game's - the whole point is that the GLSL a model draws with was decompiled out of the
game's own compiled shader binaries. When something looks wrong, the bug is almost always in how
the renderer feeds that real shader its inputs (which UBO bytes, which texture, which program
variant), not in made-up shading logic.

A BotW profile is stubbed in for future work; everything below is about the TotK path, which is
the only one actually implemented today.

**History, and how to read the rest of this file.** The renderer was developed inside the
**Marrow** viewer (`Marrow.UI`, BladesawStudios/Marrow) and split out into this library so other
tools - Prism's Actors workspace was the first - could use it. The notes below were written during
that development. Where they say "Marrow" about rendering, they mean this renderer; where they name
a UI panel (`ViewportPanel`, `InspectorPanel`, `LightingStudioPanel`, `MaterialInspectorPanel`, the
Actor Palette ...), that panel is in the Marrow viewer, which remains the reference host. Its old
`PlacedActor` is this library's `RenderActor` (Marrow's `PlacedActor` now derives from it, and adds
the cloth and helper bones back through `ModifyPose`), and its `AppState.FrameId` is the frame id a
host passes to `RenderActor.EvaluatePosedSkeleton`.

## Repository shape

- **`vendor/ShaderLibrary`** (git submodule, a separate repo) - all offline BFRES/BFSHA parsing and
  shader decompilation. Two projects:
  - `ShaderLibrary` - the parser library (BFRES, BFSHA/BNSH, TXTG, AAMP-adjacent formats).
  - `ShaderLibrary.CompileTool` - a CLI (`Program.cs`) with a grab-bag of `--flag` entry points, the
    two that matter for day-to-day work being `--prepare <Model>` (full pipeline) and
    `--rebuild-matubo <Model>` (just the material UBOs). Read `Program.cs` top to bottom before
    guessing what a flag does - it's short and every branch is a real, separately-useful step.
- **`vendor/MeshCodec/meshcodec_cli.exe`** - MeshCodec's prebuilt `.bfres.mc` decoder, which
  ShaderLibrary shells out to. It also ships beside the preparer, which points ShaderLibrary at
  that copy (`MESHCODEC_CLI`), because ShaderLibrary otherwise finds it relative to its own SOURCE
  path - a path that only exists on the machine that built it.
- **`src/WildRenderingSharp`** - the live engine: asset loading (`Assets/`), the deferred render
  pipeline (`Pipeline/`), per-frame state (`Rendering/`), per-game UBO profiles (`Shaders/`, once
  `Marrow.Shaders`), placed actors with their animation and physics (`Scene/`), and the host-facing
  layer (`Hosting/`: `SceneView`, `GLHostState`, `YUpWorld`, the preparer seam; plus `WildRenderer`
  and `CacheLayout` at the root). **Parses no BFRES/BFSHA of its own** - see below.
  `TotkShaderProfile` is thin and mostly unused for materials at runtime (see the
  `MaterialUbo`/`CreateMaterialUbo` remarks in that file) - don't start there looking for "how
  materials work."
- **No physics.** Havok Cloth and Phive Helper Bones are their own libraries - HkxSimSharp (with
  HkxSharp and PhiveSharp reading the data) and HkxHbSharp. They lived here as
  `WildRenderingSharp.Cloth` for a while and were taken out again: the renderer only ever needs the
  bones they drive. A host runs them and writes those bones into the pose through
  `RenderActor.ModifyPose` (Marrow), or poses the whole skeleton itself and passes it as
  `RenderActor.ExternalPose` (Prism). The preparer still copies an actor's `.bphcl`/`.bphhb` into its
  cache directory (`LoadedModel.DataDirectory`) so a host need not open the pack. The long history of
  getting that cloth right is in Marrow's `CLAUDE.md`.
- **`src/WildRenderingSharp.AampReader`** - AAMP parsing of the sky/cloud/colour-correction postfx,
  never referenced: loaded into its own `AssemblyLoadContext` by
  `WildRenderingSharp.Hosting.IsolatedAamp`, because it needs `Syroot.*` 5.x where BFRES needs 2.x.
- **`src/WildRenderingSharp.Preparation`** - the offline half's sequencing (`ModelPreparer`),
  usable in-process (`InProcessPreparer`) or as a child process (it is also an executable; see
  `Program.cs` for its command line, and `OutOfProcessPreparer` in the core library). A host that
  loads its OWN BfresLibrary build (Prism builds one from source) cannot load ShaderLibrary's
  vendored one beside it - same assembly name - which is the whole reason the out-of-process path
  exists.
- **`build/WildRenderingSharp.targets`** - imported by a host; builds `aampreader/` and (unless
  `WrsOutOfProcessPreparer` is false) `wrs-prepare/` beside the host's executable.

**Hosts and GL state.** The pipeline was written inside a viewer that never changed GL's global
conventions. `GLHostState.Enter(gl)` resets them (clip control, clear depth, pixel store, ...) and
restores the host's afterwards; `WildRenderer` uses it around everything it does. A host using
reversed depth (`glClipControl(LOWER_LEFT, ZERO_TO_ONE)`, as Prism does) loses the near half of
every projection here without it. The world is Z-up; a Y-up host converts at the edge (`YUpWorld`).

**The cache** (`CacheLayout`): `%AppData%\WildRenderingSharp\cache` by default; Marrow keeps its own
at `%AppData%\Marrow\cache`. Below, `<cache>` means whichever root the host uses.

## Hosts - what integrating the renderer into another tool taught

The two hosts today are Marrow (`Marrow.UI`, in-process preparation, Z-up, owns every actor's
animation and physics through `RenderActor`) and Prism's Actors workspace
(`NinTerrainViewer/Rendering/GameRendererHost.cs`, out-of-process preparation, Y-up, poses and
simulates the actor itself and hands the result over). Things that bit, in the order they did:

- **Ask for a 4.5 context explicitly.** The decompiled programs are GLSL 4.50. A driver hands back
  exactly the version requested - Prism asked for 4.3 core and got 4.3, not the 4.6 the hardware
  had - so a host that asks for less silently cannot link any game shader. Prism now asks for 4.5
  and falls back to 4.3 with the renderer off and the reason shown.
- **Restore the helper projects explicitly.** `aampreader` and `wrs-prepare` are built by the
  targets file, not referenced, so a host's restore never covers them; on a fresh clone they failed
  with "project.assets.json not found" until the targets restored them in a separate evaluation.
- **A self-contained host needs a self-contained preparer.** The targets publish one for the host's
  runtime identifier; a framework-dependent preparer beside a self-contained host will not start on
  a machine without .NET.
- **ShaderLibrary finds `meshcodec_cli.exe` relative to its own SOURCE file.** That only exists on
  the machine that compiled it. The preparer ships the decoder beside itself and sets
  `MESHCODEC_CLI`; without it, every `.bfres.mc` (including `SystemModel.DeferredMain`) fails to
  unpack on any other machine.
- **A host that poses the skeleton itself** sets `RenderActor.ExternalPose` (object-space bone
  matrices, row-vector, the shape `SkeletonPose.BindPoseWorldMatrices` returns - Prism's own
  `SkeletonPose` is a port of this one, so its `World` array is exactly that). Map bones by name;
  both come from the same BFRES skeleton and agree in order in practice.
- **Pattern clips need the preparer to have exported them**, because the textures they select are
  bound by no material and only exist in the cache if `ExportTexturePatternAnim` pulled them out.
  ShaderLibrary only walks the archives the actor's pack names, and some actors keep their patterns
  in the model's own `<Project>.anim.bfres` instead (a horse's coat variants, a Boss Bokoblin's
  colours) - so `ModelPreparer.AnimArchives` adds that archive, as Prism's own reader does, and
  `PreparationVersion` 2 makes every older cache re-prepare to pick it up. Material clips have no
  such dependency - a host's own clip data converts straight into `MaterialAnimManifest`.
- **A texture SRT clip can look frozen when it is not.** A scroll's translate is multiplied by the
  SRT's scale, so two frames whose translates differ by a whole number of repeats draw identically
  (the dungeon water flow's 400 and 900 do, at a x4 scale). Check the curve before calling it broken.
- **Prepare cancellation.** A host abandons a preparation when the user moves on; out of process
  that means killing it. `Prepare` deletes the manifest and source stamp first, so a killed run is
  never taken for a finished one.
- **Drawing over the frame.** Prism blits `SceneView.OutputFramebuffer` into its own target and then
  draws its overlays (grid, skeleton, colliders, outline, gizmo) with its own copy of the actor drawn
  depth-only, which is what keeps them occluded correctly. Depth from the renderer cannot be shared
  directly - the conventions differ (standard `[-1, 1]` here, reversed float in Prism).

## Everything happens twice: offline, then live

This is the single most important thing to understand before touching anything.

**Offline** (`ShaderLibrary.CompileTool`, run once per model via `ModelPreparer.Prepare` in
`WildRenderingSharp.Preparation` - in-process, through `InProcessPreparer`, or as a child process,
`WildRenderingSharp.Preparation prepare --romfs <romfs> --actor <name>`, through
`OutOfProcessPreparer`; Marrow also exposes it as `Marrow.UI --prepare <romfs> <Model>`):

1. `ExportTestBench.ExportModel` - geometry, textures, skeleton/anims, straight off romfs.
2. `BuildMaterialUbo.Run` - for every material, starts from its shading model's compiled
   `gsys_material` block DEFAULT bytes, then overlays the material's own `ShaderParams` **by
   name** (the shader's own byte offsets and the material's packed blob offsets are different
   address spaces - joining by name is the whole job; see the class doc, it explains a real
   incident where a live-captured-from-game byte source turned out to be garbage). Writes
   `matubo/<Material>.gsys_material.bin` (raw bytes, no schema) and a `.params.json` name->offset
   sidecar (needed only for material-parameter *animation*, not for a normal draw).
3. `ExportManifest.Run` - resolves, per shape, which compiled **shader program variant** the real
   game would pick (`TestTOTK.GetShaderProgram`, an exact key-table lookup against the material's
   own `ShaderAssign.ShaderOptions` with a documented, conservative "closest program" fallback for
   near-misses - read the remarks on `ResolveViaClosestProgram`/`NormalizeOptionsForShader` before
   assuming a resolved option is wrong), decompiles that program to real `.vert`/`.frag` GLSL
   (`ShaderExtract.GetCode`, Tegra shader assembly -> GLSL), and writes one
   `<Model>.manifest.json` describing every shape: vertex layout, which program/shader file backs
   its G-buffer/Z-only/forward draw, its sampler bindings (shading-model sampler name -> material's
   texture, by name join, same principle as step 2), its render state, and its `o_material_behave`
   value (which also selects a shared deferred-resolve pass - see below).

Output lands in `<cache>\<ResolvedModelName>\` (one subdirectory per model - two
models sharing a same-named shape/texture used to clobber each other before this), with decompiled
shader programs shared across all models in `<cache>\_shaders\` (keyed by shading
model + program index into the same `material.bfsha`, safe to share since the archive is fixed).

**Live** (`WildRenderingSharp`, every time a model loads or a frame renders): reads the manifest JSON,
loads the `.bin` vertex/index/texture/material-UBO files as-is, links the **already-decompiled**
`.vert`/`.frag` off disk (`ShaderProgramCache.Load`, with `GlslSanitizer.Clean` doing only cosmetic
NVN-extension-stripping/NaN-guarding, no logic rewriting), and draws. There is no BFRES/BFSHA
parsing anywhere in this half - `MaterialUbo`/`TotkShaderProfile.CreateMaterialUbo` in
`WildRenderingSharp.Shaders` are vestigial for this reason; `ModelLoader.cs` reads material bytes itself instead
of going through them.

Linking is the expensive part of a live load (1-2s for a character's programs, on the GL thread),
so `GLProgramBuilder` keeps every linked program's driver binary in `<cache>\_glprograms\`, keyed
by both GLSL sources and the driver's vendor/renderer/version, and loads that instead of compiling.
A binary the driver rejects is compiled again and replaced. Deleting the folder only costs one slow
load per program. If a decompiled shader looks unaffected by a change you made to its GLSL, the key
is the sanitised source, so it cannot be serving a stale binary - look elsewhere.

**Practical consequence for debugging:** the hand-written reference shader at
`vendor/ShaderLibrary/ShaderLibrary.CompileTool/Shaders/TOTK/Pixel.frag` (and its `Vertex.vert`
siblings) is **not what renders** - it's a hand-authored, admittedly-incomplete reconstruction kept
around as a readable map of the *general* shape of the real shading model (uniform block layout,
the `o_material_behave` switch's rough intent). The ground truth for any specific material is the
**real decompiled file** the manifest names (`<shape>.gbuffer_shader` -> a file in
`<cache>\_shaders\`). Always go read that file for the actual shape you're
debugging before theorizing from the reference file or from first principles - the real one is
short (~150-250 lines), fully expanded (no macros/branches left unresolved - the decompiler already
baked in every static option), and is the only thing that can't be wrong about what the game itself
does.

## Mods (`RomfsOverlay`) - per-file layering over the base romfs

`ShaderLibrary.CompileTool.RomfsOverlay` layers mod romfs folders over the base dump file by file
(highest priority first), like an emulator's LayeredFS. Because replacement is per FILE, a
texture-only mod applies to an unmodded model: the model resolves from base `Model/`, each
`TexToGo/` lookup independently picks up the mod's copy. Routed through it: actor packs
(`ActorInfo`, `ExtractPhysics`), `RomfsPaths.ModelFile`, anim archives, every `TexToGo/` lookup in
`ExportTestBench`/`ExportManifest`/`ExportTexturePatternAnim`, `ExternalBinaryStringTable`, and
`ActorCatalog` (mod-added actors appear in the palette, tagged `[mod]`). **Deliberately not
routed**: `Shader/*.bfsha` and the agl/system archives - `_shaders` and `_romfs_decompressed` are
keyed on those being the fixed shipped archives. The mod list lives in settings (`totk_mods`),
edited under Edit > Mods..., applied by `ModManager.Apply`. `FindModRomfs` accepts `romfs/` and
`romfslite/` (TKMM's "Use romfslite" merge output - same layout, renamed folder). Point it at
TKMM's MERGED OUTPUT folder, not its per-mod storage. Copy/paste in ImGui text boxes depends on
`ImGuiTextInputFix` (Silk's controller sends no `ImGuiMod_*` events and installs no clipboard). A new lookup site in the prepare path
must use `RomfsOverlay.Resolve`, not `Path.Combine(romfsRoot, ...)`, or mods silently won't apply
to it.

**Cache staleness**: `ModelPreparer.Prepare` records every romfs-relative path it resolved (and
which layer won, including misses) into `<model>/romfs_sources.json`; `IsUpToDate` re-resolves
those paths and the palette re-prepares when anything differs (mod toggled, reordered, or a mod
file edited). A cache predating this has no stamp and is trusted only while no mods are active.
Actors already placed in the scene are not hot-swapped.

**Loading by texture**: romfs only records model -> texture, so `TextureModelLookup` (UI) builds
the reverse from `TextureModelIndex` (CompileTool): decompress every `Model/*.bfres.mc` through
MeshCodec and keep NUL-terminated string-pool tokens that are real `TexToGo` names (texture refs
are plain strings in the decompressed BFRES - no BfresLibrary parse needed). Base game: ~110s once,
cached in `_texture_index/base.json` (keyed on romfs path + Model/TexToGo counts). Enabled mods'
own models are rescanned per mod-set change and override base entries by stem. Models per texture
are ranked by shared name prefix; the Mods tab's texture rows load the best match (or offer a
picker when a texture is shared).

## Key concepts worth knowing before you dig

- **`gsys_material` (binding 8)** has no fixed layout - every shading model declares its own block.
  `MaterialUbo` is deliberately just a raw byte wrapper. A material's `.params.json` sidecar gives
  you name->offset if you need to read/write a specific field (see `MaterialAnimPose.cs` for the
  established read-modify-`glBufferSubData` pattern used by material-parameter animation).
- **`o_material_behave`** is a per-material static shader option, baked at compile time, that picks
  a branch in the G-buffer shader (skin/hair/eye/metal/etc.) **and separately** selects which
  shared deferred-resolve-pass shader re-shades that surface after the G-buffer
  (`ExportManifest.BehaveToPass`, `DeferredResolvePass.ResolveDeferredPasses`). Don't assume a
  given creature's eye uses `104`/`chara_eye` just because that's the "eye" value in the reference
  shader's comment - real materials sometimes genuinely use a different behave value (e.g.
  `10`/`chara_grossy`) for what looks like an eye. Check the manifest, don't guess from the name.
- **Texture formats**: TotK ships `.txtg` containers (`TxtgTexture.cs` - zstd-compressed,
  Tegra-X1-block-linear-swizzled surfaces; `TegraX1Deswizzle` unswizzles to linear
  block-compressed bytes, uploaded straight to GL via `glCompressedTexImage2D` with no CPU
  decode, except ASTC which desktop GL can't be trusted to support and is CPU-decoded instead).
  `TextureCache.ApplySwizzle` sets a GL component swizzle for single-channel BC4 textures
  (`RRRR` - matches the game's own NVN swizzle; without it a BC4 mask reads red-only, which is
  the "Link's eyes" incident the comment references). **Do not reintroduce a BC5 blue/alpha
  override** - GL's own default identity swizzle for a two-channel compressed format already
  reads as `(R, G, 0, 1)`, which is what every real shader in this corpus expects; a prior version
  of this method forced BC5's blue to `1.0` on an unverified "BC5 always means normal map" theory,
  which silently injected a wrong blue channel into any BC5 texture used as color data instead
  (confirmed: `Cmn_Enemy_DungeonBoss_Eye_Alb`, the iris texture shared by `Enemy_Drake` and
  `Enemy_MiasmaTentacle`). That fix is landed; it is **not**, on its own, a complete fix for those
  two creatures' eye colour looking wrong - see Known Issues.
- **A material's `ShaderOptions` choices are resolved once, offline, into one specific compiled
  program per shape** (see `TestTOTK.GetShaderProgram`). There is no runtime "pick a different
  shader variant" switch in the live viewer - the manifest names exactly one G-buffer/Z-only/
  forward program per shape, permanently, from the moment the model is prepared. A live "material
  editor" can only ever expose the *numeric/color UBO fields* of the variant already baked in, not
  a different variant.
- **The Ghidra MCP has the actual game binary loaded.** `docs/gsys_environment.md` is a worked
  example of using it to recover a real uniform-block declaration by decompiling
  `gsys::ModelRenderContext::initialize` and friends - the same technique applies to any other
  "what does the game actually do here" question a shader corpus alone can't answer (e.g. a
  deferred-resolve pass's exact lighting math, a texture's real intended channel semantics).
  `totk-mcp` is the tool for reading/dumping romfs assets directly (BYML, SARC, AAMP, texture
  containers) when you need to cross-check something outside what `ShaderLibrary.CompileTool`
  already parses.

## Debugging workflow that actually works

1. Find the shape in the model's `<Model>.manifest.json` (`<cache>\<Model>\`).
   Note its `material`, `o_material_behave`/`deferred_pass`, `gbuffer_shader`, and `samplers` list
   (unit -> shading-model sampler key -> real texture name/format).
2. Read the **real** decompiled `.frag`/`.vert` the manifest names, in
   `<cache>\_shaders\`. Trace exactly which `cTextureN` (-> which real texture, via
   the manifest's sampler list) and which `fp_c11.data[N]` (`gsys_material` slot - cross-reference
   `matubo/<Material>.params.json` for the name at that byte offset) feed the pixel you care about.
3. If a texture's *content* is suspect, dump it independently of Marrow's GL upload path
   (`ShaderLibrary.CompileTool --dump-txtg <TextureName> <mip> <outPath>` writes the raw,
   already-deswizzled block-compressed bytes to disk) and decode it with an independent tool
   (Switch Toolbox, or a throwaway decoder) - if it matches what a trusted independent tool shows,
   **the bug is not in texture decode**, and you've just ruled out an entire category by comparing
   data, not by rendering anything.
4. Only after the manifest/shader/texture-data trail is exhausted does it make sense to ask the
   user to run the interactive app and describe/show what they see. **Do not build ad-hoc
   screenshot/headless-render tooling to "verify visually" yourself** - a `Marrow.UI` GL context
   makes state assumptions (camera framing, model orientation, which pass a shape resolves through)
   that are easy to get subtly wrong in a one-off harness, burning a lot of turns on results that
   look plausible but aren't trustworthy. The user runs the real app and can tell you exactly what's
   still wrong far faster than a bespoke capture tool can. If you *do* have a good reason to render
   something programmatically, say so and ask first rather than treating it as the default move.
5. `MC_DEBUG_OPTIONSEARCH=1` as an environment variable around any `CompileTool` invocation that
   calls `TestTOTK.GetShaderProgram` dumps every static/dynamic option's supplied-vs-default value
   for the shading model in play - useful when a shape resolves to a surprising program variant.

## The deferred-resolve passes have their own shared `gsys_material`, and it was never built

`o_material_behave` selects a shared deferred-resolve-pass shader (`chara_skin`, `chara_hair`,
`chara_eye`, `chara_grossy`, `chara_nonmetal`, `chara_metal`, `preshading_*`) that re-shades a
G-buffer surface after the fact. That shader has its OWN `gsys_material` block - a completely
different one from the object's own material, built from the shared `system.*.bfsha` +
`SystemModel.DeferredMain.bfres`/`.bfres.mc` (each Shape in that model is one deferred pass,
literally named `chara_skin` etc.). Until this session, **nothing in `ModelPreparer.Prepare` ever
built it** - `DeferredResolvePass.ResolveDeferredPasses` looked for
`<dataDirectory>/matubo_deferred/<pass>.gsys_material.bin`, that path never existed for any
normally-prepared model, and it silently fell back to an all-zero block. Fixed this session:
`BuildMaterialUbo.RunSystemDeferred` builds it (decompressing `system.*.bfsha.zs` and
`SystemModel.DeferredMain.bfres.mc` straight from romfs), `ModelPreparer.EnsureSystemDeferredMaterials`
builds it once into a shared `_deferred_materials` cache directory (sibling of `_shaders` - it's the
same data regardless of which model is loaded), and `DeferredResolvePass`/`DeferredPipeline` read
from there instead of the per-model directory. A `--rebuild-deferred-materials` CLI flag forces a
rebuild.

**Consequence for debugging**: before this fix, ANY deferred-resolve-only parameter (a resolve
pass's own tint/rim/miasma-ish constant - anything not read by the object's own G-buffer program)
was always zero no matter what the object's material said. If you're chasing a "this material
parameter does nothing" bug, first check whether it's read by the DEFERRED shader
(`deferred_<pass>_prog*_extracted.frag`, a different file with a different `gsys_material` block
than the object's own G-buffer program) rather than assuming the object's own material is what
matters - and confirm the shared `_deferred_materials` directory in the cache actually has real,
non-all-1.0-default values for the pass in question (evidence it was actually built, not stale from
before this fix).

**A "does nothing" parameter needs checking against ALL THREE of a shape's resolved programs
(G-buffer, Z-only, forward/`gsys_assign_material`), not just G-buffer** - `p_miasma_ratio` looked
completely unused on `Enemy_MiasmaTentacle`'s `Arm_Model__Mt_Skin` when only the G-buffer program
was checked, but its FORWARD program (`gsys_assign_material`) genuinely reads it, gating a
procedural-noise dissolve effect. The automated used/unused detection below checks the union of all
three programs for exactly this reason - grep a parameter's offset (`offset / 16` = slot,
`offset % 16` -> `.x`/`.y`/`.z`/`.w`) as `fp_c11.data[N]` across
`<shading>_prog<gbuffer|zonly|material>_extracted.{vert,frag}` before concluding a parameter is
genuinely unused anywhere.

**`gsys_assign_material` (the "forward" program) is not PURELY blend-only, but running it broadly
turned out to be genuinely risky, and it's opt-in now, off by default.** The original assumption -
build/draw it only for blended shapes, since a translucent surface can't use the deferred G-buffer
anyway - is too narrow: reading the real, full forward program for several OPAQUE materials
(confirmed on `Enemy_MiasmaTentacle` and `Enemy_Bokoblin`) shows it's a complete independent
forward-lit shader (its own shadow-cascade/sky-projection sampling) with branches keyed on
`p_miasma_ratio`/`p_kari_mottled_ratio`/`p_kari_chemical_ice_ratio0`/`p_proc_vanishing`/
`p_damage_color` etc. that recompute the albedo when a status effect is active - a real
status-effect compositing pass the game may run over opaque objects too, not strictly a
blend-only path. But making Marrow run it unconditionally for every opaque/masked shape that
resolves one caused two confirmed regressions in a row:
1. A Y-flip bug (fixed, worth understanding on its own - see below).
2. A full-body bright-cyan oversaturation on `Enemy_MiasmaTentacle`/a Ganondorf model (NOT fixed).
   Traced (see the exact byte-level walkthrough in `tasks_set1.md`) to a real "Ganon soul
   corruption" glow effect this material's forward program applies via an extra LUT texture and a
   real, authored `3.0x` intensity multiplier - the cyan colour itself may be intentional, but the
   leading (unconfirmed) theory is that this pass's linear-HDR output gets the SAME blanket
   exposure/tonemap treatment as the deferred-lit result, with no compensation - unlike
   `DeferredResolvePass`'s emission term, which has an explicit divide-back for exactly this
   reason (see that class's own remarks) - so anything already "complete, don't scale further"
   this shader outputs blows out catastrophically under Marrow's 10x deferred-calibrated exposure.

Given two confirmed breakages from one blanket change with no way for me to render/tune a fix
myself, this was reverted to OPT-IN: `LightingContext.ForceForwardOnOpaqueShapes` (default
**false**) gates it, exposed as an experimental checkbox in `LightingStudioPanel`
("Forward pass on opaque shapes (experimental, may look wrong)"). Normal viewing is back to the
original blend-only behaviour. `ModelLoader.cs` still builds forward VAO/program/samplers for
every resolved shape unconditionally regardless of blend (harmless - it's just readiness, not a
draw), so flipping the toggle takes effect the very next frame with no reload needed. If picking
this back up: confirming the exposure theory (and finding where to hook a compensating scale, given
this pass's output is blended directly into the same buffer as the deferred result at the
framebuffer level - not a separate scaled layer the way G-buffer emission is) is the open question.

**The Y-flip bug** (fixed, independent of the above and worth understanding on its own - the same
flipped/unflipped-context distinction bites elsewhere in this pipeline): `ForwardPass` rasterises
into `targets.Scene`/`targets.GBufferDepth` - the G-buffer's Y-FLIPPED orientation (`targets.Scene`
only exists as a Y-flip of `targets.Final` into that space, specifically so the forward draw can
depth-test against `GBufferDepth` at all). But the shared Context UBO (binding 1) had been left at
`ctx_true` (the TRUE/unflipped projection) ever since right after the G-buffer pass, and nothing
rebound it before the forward draw loop - so the forward geometry was transformed in the wrong
(unflipped) space while the background around it was flipped, and the pass's own final `FlipInto`
(which flips the WHOLE `Scene` buffer back to `Final`) then flipped the newly-drawn geometry an
unwanted SECOND time, leaving it a vertically mirrored duplicate of itself over the correctly-
restored background. Invisible before because almost nothing reached this pass (blend-only);
glaringly visible once opaque shapes briefly did too (a Bokoblin rendered as two mirrored copies
meeting at a horizontal seam). Fixed by having `ForwardPass.Run` explicitly
`resources.BindUbo("ctx_gbuffer", 1)` before its draw loop and restore `ctx_true` itself afterward,
rather than trusting whatever the caller happened to leave bound. This fix stayed even after the
broader change was reverted to opt-in - it's correct for the (still opt-in) forward-on-opaque case
and for genuinely blended materials alike.

## Known open issue: eyelash alpha cutout brightens what's behind it (Link/Zelda) - deprioritized

`Eyelashes__Mt_Eyelashes` (mask/alpha-tested, not blend) correctly cuts to the lash-strand shape,
but wherever a texel is discarded, the face/skin behind it renders BRIGHTER than plain skin should,
instead of just showing the correct, unaffected skin. Deprioritized by the user ("so minor idgaf") -
not investigated in depth. `GBufferPass.cs`'s Z-only-prepass-then-G-buffer-EQUAL-test mechanism
(the real, intentional way a mask material's cutout works here - see that file's own remarks) looks
architecturally sound by inspection; didn't find an obvious bug in it. Leading unconfirmed guesses:
a screen-space AO/shadow halo at the hard depth discontinuity the cutout edge creates, or bloom
bleeding from the (real, and plausibly intentionally glossy) surviving lash-strand pixels onto
adjacent skin.

## "System" textures: one real asset found (`cTex_Proc3DNoise`), the rest are dynamic subsystems Marrow doesn't have

The forward/deferred-resolve shaders reference several sampler names with no owning material
(`cTex_Proc3DNoise`, `cTex_SkyInscatter`, `cTex_SkyIslandShadow`, `cTex_MinusFieldDarkness`/
`LightMap`, `cTex_WorldShadowHeight`, `cTex_Projection0`, `cTex_ForwardTerrainAlbedo`,
`cTex_DepthShadowCascade`) - shared, engine-level resources every draw needing that effect binds
the same way, previously all just flat placeholder constants in `ForwardPass.cs`
(`_texWhite`/`_texVolumeMask`/etc.). Only ONE of these is a genuine static romfs asset:
**`cTex_Proc3DNoise`** is `TexToGo/3DWorleyPerlinNoise_Fi.bntx.zs` - a real 64x64x64 3D
(`Dim3D`) BC4_UNORM Worley/Perlin noise volume. Every other name on that list is a dynamic,
camera/world-state-dependent render target (shadow cascades, sky atmospheric scattering, the
Depths' darkness maps, streamed terrain) - there is no file to find for those, only a whole
subsystem Marrow doesn't implement at all; don't go looking for a romfs asset behind them.

Two things make this asset extraction different from every texture path elsewhere in this codebase:
- It's a plain **`.bntx`** (vanilla Switch container, zstd-compressed with no dictionary), not
  TotK's own `.txtg` wrapper (`TxtgTexture.cs`) every material texture uses. Parsed via
  `Syroot.NintenTools.NSW.Bntx.BntxFile` (already an indirect dependency via `BfresLibrary`, and
  already exercised once by the pre-existing `--dump-bntx` CLI flag for icon extraction) after a
  plain `TotkCommon.Zstd`/`Totk.Zstd.Decompress`.
- It's a genuine 3D (volume) texture, not 2D - `TxtgTexture.cs`'s `TegraX1Deswizzle.Deswizzle`
  hardcoded depth=1 in its native `deswizzle_block_linear` call; `Deswizzle3D` (new) generalizes it
  by passing the real depth straight through (BC4 has no Z-blocking, so depth in the deswizzle call
  is genuine texel depth, not divided by any block factor - only width/height are).
- Decoded to plain single-byte-per-texel R8 on the CPU (`SystemTextures.DecodeBc4Volume`/
  `DecodeBc4Block` - the standard DXT5-alpha-style 8-bytes/4x4-block scheme, hand-written since
  nothing in this codebase decoded BC formats on the CPU before) rather than uploaded as a
  compressed 3D texture - desktop GL's RGTC extension doesn't guarantee 3D compressed-texture
  support the way it does 2D, and decoding on the CPU once at extraction time is cheap enough that
  "always works" beats "usually works." **Verified the decode is correct by rendering a middle
  Z-slice to an image** (not just "didn't throw") - it's unmistakably real, smoothly blobby,
  correctly-tiling cloud-style noise.

`ModelPreparer.EnsureSystemTextures` extracts this once into a shared `_system_textures` cache
directory (same pattern as `_deferred_materials`/`_shaders`); `ForwardPass`'s constructor
(`LoadRealNoiseVolumeOrFallback`) uploads the real volume with the same R-channel-broadcast
swizzle every other BC4-sourced texture in this codebase gets, falling back to the old flat
mid-grey placeholder only if extraction hasn't run yet. If you need to find a similar asset for a
DIFFERENT "system" sampler name in the future: search `TexToGo/` directly by the CONCEPT the
sampler name implies (not the literal name - shader reflection names and romfs asset names don't
match), and confirm with `--inspect-bntx`/`--test-txtg` before assuming it's the right one; most
of the remaining names on the list above are not going to have one.

## The real cloud dome (`agl::fx::Cloud`) - a fourth asset source, and the only one driven from a GPU capture

The sky/cloud path does not come from BFRES/BFSHA at all. It comes from **agl** shader archives
(`.sharcb`, magic `BAHS`) inside `Lib/agl/agl_resource.Nin_NX_NVN.release.sarc.zs` - a completely
separate archive family from `material.bfsha`, with its own container parser
(`TestAglShader.cs`). Everything below follows the same offline-then-live split as the rest of the
project, and **every step is automated from romfs** - nothing here was hand-placed into the cache,
because the whole thing has to work on someone else's machine from their own ROM.

**Offline** (all guarded on "already extracted?" + "romfs present?", so they're free on later runs):
- `TestAglShader.ExtractCloudShader` / `ExtractCloudNoiseShader` - decompile the real `cloud` and
  `noise_cloud` programs to `agl_cloud.vert/frag` and `agl_noise_cloud.vert/frag`. Mirrors the
  pre-existing `ExtractHdrCompose`; picks one specific macro combination out of the archive's
  variant matrix. CLI: `--extract-cloud-shader`, `--extract-cloud-noise-shader`.
- `SystemTextures.ExtractCloudTextures` - `PolarSphereMappingNoise_Fi` -> `CloudBase`,
  `VolumeMist03` -> `CloudNoise`. Both 512x512 BC4_UNORM, CPU-decoded to R8 alongside a
  `.dims.txt`. **`TxtgTexture`'s `Surfaces[0].Data` is ALREADY deswizzled** - calling
  `Deswizzle` on it again silently produces garbage (cost real time once; don't repeat it).
- `ModelPreparer.EnsureCloudShader`/`EnsureCloudNoiseShader`/`EnsureCloudTextures`, called from
  `ViewportPanel`'s constructor, into the shared `_system_textures` cache.

**Live**: `CloudDomePass` (run from `DeferredPipeline` right after `_background.Run`, gated on
`LightingContext.UseRealCloudDome` - default **false**, exposed as "Real cloud shader
(experimental)" in `InspectorPanel` under TotK Sky).

Three things about this pass are non-obvious enough to be worth stating outright:

- **Geometry is procedural, not a mesh asset.** `CloudDomeMesh` is a direct port of the real
  `Cloud::initVertex_`: 24 segments per ring, `radius = 1 - t^3`, a height-flatten branch
  (`circleHeight <= 0.1 ? (0.1 - ch) * -0.3 + 0.1 : ch`), `y = height - 0.07`, apex at `(0,1,0)`.
  Verified **byte-exact** against the RenderDoc capture's own vertex buffer (289 verts / 1656
  indices near, 3384 far). The local mesh is **Y-up**; Marrow's world is **Z-up** - the model
  matrix in `BuildViewBlock` does that swap (row 1 carries `radius` in Z, row 2 carries `height`
  in Y-of-local -> Z-of-world). Getting this backwards renders a dome lying on its side.
- **The UBO layout was recovered from a capture, not from reflection, and it starts from captured
  bytes.** `CloudUboBaseline` holds the real 768-byte Common block from the capture as hex
  literals, and the pass overwrites only the ~40 slots whose meaning is established (see the
  per-slot comments in `CloudDomePass`). This is deliberate, not laziness: the shader computes
  `1.0/data[5].w` and `1.0/data[26].w`, so a zero-initialised block yields infinity -> NaN ->
  nothing rasterises at all. Do not "clean up" the baseline to zeros.
- **Cloud COLOUR comes from EnvPalette (`Cloud0`/`Cloud1`), not from the AAMP.** The
  `.baglclwd`/`.baglsky` postfx files are static shape/density/falloff parameters; the per-palette
  colours are separate. This is why an earlier version "only worked on some palettes" - it was
  reading colour from the static source. The capture's palette was identified as
  `Prequel_MainField_Bluesky_3_Noon` by byte-exact colour match, which is what made the whole UBO
  mapping checkable numerically (191 of 192 floats reproduce bit-exactly).

Per-stage NVN constant-buffer indices collide once both stages are linked into one desktop-GL
program (`_vp_c4` and `_fp_c3` are the same logical block), so the pass reassigns them explicitly
via `glGetUniformBlockIndex` + `glUniformBlockBinding` rather than trusting the declared bindings.

**THE CLOUD MASK TEXTURES ARE WRONG - CONFIRMED, not suspected.** `PolarSphereMappingNoise_Fi`
(`CloudBase`) and `VolumeMist03` (`CloudNoise`) were picked by searching `TexToGo/` for a plausible
NAME at the right size/format, and both are wrong. Proof: the real cloud draw was located in a GPU
capture (eid 2040 - identified by its 768-byte `Common` block plus 4 samplers, since draws are all
named `vkCmdDrawIndexed()`), its bound textures dumped, and compared byte-wise against what Marrow
extracts. Correlations are ~0.01-0.10, i.e. unrelated images.

What the game actually binds is **three distinct 512x512 BC4_UNORM textures**:
`cBaseTexture` and `cBaseTexture_Blend` are the SAME texture, while `cNoiseTexture` and
`cNoiseTexture_Blend` are DIFFERENT from each other (Marrow currently binds base twice and noise
twice, so the blend slot is wrong too).

**They are almost certainly not romfs assets at all.** A byte-exact content scan of all ~29,000
`TexToGo/*.txtg` files (`--find-txtg-by-bytes`, resumable because a few files panic the native zstd
decompressor and kill the process) found NO match - best score 37.9%, which is noise. That fits what
this file already documents above: the cloud noise resolves through a runtime name->texture
dispatcher to a dynamically-baked resource called `cloud_noise`, not a file. The two different noise
textures line up with `noise_cloud`'s own `RENDER_TYPE[0|1|2]` macro - i.e. the same generator baked
twice with different parameters.

**So the fix is to RUN `agl_noise_cloud`, not to keep hunting romfs.** That program is already
extracted (`ExtractCloudNoiseShader`) and sitting in the cache unused; baking it the way
`SkyPrecomputePass` bakes the sky LUTs is the path, and it also settles that shader's admittedly
unconfirmed `RENDER_TYPE` choice, since the two baked results can be compared against the dumped
real textures. The dumps are kept at `RenderDocMCP/cloudtex_*.bin` as ground truth.

**The dome renders at TRUE scale now, via `GL_DEPTH_CLAMP`.** It used to be shrunk to fit inside
Marrow's 4000-unit far plane (`0.45 * farPlane / SkyScale`), and that is what broke the distance
fade: the shader's far-fade constants are calibrated against the real ~26500-unit dome, so at ~7%
scale every fragment landed in the "near" bucket. The symptom was that the fade only reappeared as
the far plane was pushed out. The real game does not scale anything - `agl::fx::Cloud` sets
`mIsDisableFarClip` - and `GL_DEPTH_CLAMP` is the direct equivalent: it clamps depth rather than
clipping geometry, so the dome can sit beyond the far plane at its authored size. Note the clip
happens in the rasteriser whether or not a depth buffer is bound, so this is needed even though the
pass has no depth attachment.

**The cloud draw is alpha-blended, not additive** - confirmed from the capture's own state:
`SrcAlpha / InvSrcAlpha, Add` for colour, with depth test `LessEqual` and depth writes OFF. The
ALPHA factors are `(Zero, InvSrcAlpha)`, which Marrow had as `(One, Zero)`; that overwrote the
destination alpha with the cloud's own instead of leaving `1-srcAlpha` of it, which matters
wherever that alpha is read later (transparent-background export in particular).

**Check the real UBO before changing a cloud slot.** The whole 768-byte `Common` block is readable
from the capture at the real cloud draw (eid 2040 in `GoodExampleTotK.rdc`, found by its 768-byte
`fp_c3` plus 4 samplers). Three of Marrow's own writes disagreed with it and were only found this
way: `[42]` is the sun direction **negated** (the direction light travels), `[8].w` is `-1` where
`mFarAlphaChgPower` parses to `-0.5` (so that write was removed - it replaced a correct captured
value with a wrong one), and `[25].y` is `CloudColorScale` raw, i.e. the game applies no exposure
correction there. `[45].x` really is `0`.

**Marrow adds its OWN cloud distance fade** (`CloudDistanceFade`), because the game's own never
produced a visible falloff here no matter how its slots were driven - the real one is
`alpha -= clamp((farness - FarAlphaChgStart) / FarAlphaChgEnd)` fed through
`in_attr1.w = temp_100 * Common[5].w`, and Common[5] is a slot Marrow only ever inherits from the
captured baseline. Rather than keep guessing at blob slots, the fade is an explicit GLSL patch with
user controls (start distance, ramp, exponential/linear, amount; amount 0 disables it entirely).

The patch technique is worth knowing generally: the decompiled `main` is renamed to
`mrw_inner_main` and a NEW `main` calls it and then adjusts the outputs. Every original instruction
still runs unmodified in its original order, so this cannot disturb the real shader - and it needs
no understanding of the shader body at all. The vertex half adds one varying carrying the dome's
UNIT local position, which multiplied by the dome extents gives the view distance directly (the
dome is always centred on the eye), sidestepping every matrix/Y-up question. `ShaderProgramCache.Load`
takes optional per-stage source transforms for this, and bypasses its program cache when patched so
two callers cannot share one linked program.

Distant cloud fades toward the sky colour as well as losing alpha - fading alpha alone reads as
"thin cloud", not "far cloud".

**Still approximate** (don't present these as solved): `cScatterTexture` is flat because Marrow has no real atmosphere bake;
placement-point fades keep their captured constants; and the dome is uniformly scaled to fit the
far plane (`0.45 * farPlane / SkyScale`, capped at 1) because Marrow can't honour the real
`mIsDisableFarClip`.

Capturing more of this yourself: **Ryujinx will not launch under RenderDoc's normal injection** -
its Avalonia/ANGLE front end crashes. `renderdoccmd.exe capture ... --software-gui` works.
"Attach to Running Instance" is the remote-host manager, not a local attach; "Inject into Process"
is disabled by default and unreliable. The relevant draws in the capture used here were pass 40
(clouds) and pass 39 (sky).

## The horizon fog band (`USE_ADHOC_FOG`) - IMPLEMENTED, and what it taught

`sky_postfx_sky` carries a `USE_ADHOC_FOG` macro that adds **no sampler and no block** - same
`Context@224` / `RenderInfo@112` / `cTexBakedInscatter` as the plain variant, only more pixel
bytecode. So it reads its parameters out of Context slots the other variant never touches, and
those were recovered by extracting BOTH variants and diffing which slots the `=1` one newly reads:
exactly `[10].y/.z/.w` and `[11].xyz`, plus `[10].w` again in the vertex stage. The decompiled
program is:

```
master = sqrt(clamp(C[10].w * 4, 0, 1))          (vertex -> in_attr1.x)
up     = clamp(viewDir.y, 0, 1)                   0 at the horizon, 1 at the zenith
scale  = mix(C[10].w, C[10].z, pow(up, C[10].y))  horizon end -> zenith end
rgb    = mix(skyColour, C[11].xyz, scale * master)
```

**The names decode cleanly onto the palette and the AAMP.** `master_field.baglsky`'s `sky` object
has a whole `adhoc_fog_*` group, and the ResEnvPalette field prefixes are abbreviations of it -
"Af" = **A**dhoc **F**og, "Sf" = **S**catter **F**og:

| palette | `master_field.baglsky` |
|---|---|
| `AfParam_attenuationForGrd` / `ForSky` | `adhoc_fog_atten_grd` / `_sky` |
| `FogColor` / `FogStart` / `FogEnd` | `adhoc_fog_color` / `_near` / `_far` |
| `SfParam_attenuation` / `_horizontal` / `_near` | `scatter_fog_atten` / `_horz` / `_near` |

**Verified against a real capture** (eid 2016 in `GoodExampleTotK.rdc`, identified by `fp_c1`
holding the sky shader's own acos constants). Context read:

```
[ 8] 40.0,  2.5,  0.85, 0          scatter_fog_atten / _horz / _density, matching the AAMP
[10] 4.0,   0.5,  0.3,  0.089538   atten_grd=4 and minscale_sky=0.3 match the AAMP exactly
[11] 0.585, 1.000, 0.806, 1.0      Prequel_MainField_Bluesky_3_Noon's own FogColor, BIT-EXACT
[13] 1.0                           the game's own sky intensity
```

`[11]` matching the palette bit-for-bit is what settles the slot AND settles that the colour goes
in **raw**, not normalised. Two things the capture CORRECTED rather than confirmed: `[10].y` is
**0.5**, neither the AAMP's `adhoc_fog_atten_sky` (0.764) nor the palette's
`AfParam_attenuationForSky` (0), so its real derivation is unknown and the measured value is used;
and `[10].z` is the minscale **raw**, not scaled by density.

**Which end is which is what makes one formula do both looks.** The mix runs from `[10].w` at the
HORIZON to `[10].z` at the ZENITH. At noon `0.0895 < 0.3`, so the pale fog sits mostly overhead - a
gentle high haze. Under a blood moon the palette authors `FogColor` alpha `0.6`, so `0.6 > 0.3` and
the same mix runs the other way: saturated colour piled at the horizon. That is the red band, and
it falls out of the palette rather than being special-cased.

Two things remain unconfirmed and are named as such in `SkyPostFxPass.Resolve`:
- **Density's CPU derivation.** `FogColor`'s alpha is clearly it for palettes that author one, but
  noon authors 0 while the capture shows 0.089538, so something floors it. Taking the larger of the
  two reproduces the captured noon frame and still gives a blood moon its band.
- **Raw vs normalised colour.** Raw is what the capture shows, but authored magnitudes differ 17x
  between palettes (`BloodyMoon_DarknessDragon`'s `FogColor` is `(0.035, 0, 0.008)`), so raw leaves
  its band at ~3% of sky peak - near black, not red. `LightingContext.SkyFogNormaliseHue` (default
  **true**) offers both; noon is identical either way.

`C[10].y` is a `pow` exponent, so it must never reach the shader as 0 - `exp2(log2(0) * 0)` is NaN
at the horizon, the same trap `EnvUbo.PowExponentSlots` documents.

## Sun, moon, lens flare, stars - sun/moon/flare BUILT, the rest located

None of these are missing data; they are unbuilt passes. What the ROM actually has:

**Built this session** (`SkyBodyPass`, `LensFlarePass`):

- **Sun and moon are drawn from the game's own sprites**, not the shader's `RENDER_SUN` disc.
  `Etc_Sun_A_Alb` is a 64x64 BC4 disc MASK (single channel - the colour comes from the palette's
  own `SkySunColor`, normalised to a hue, which is why one texture covers every time of day), and
  `Etc_Moon_A_Alb.1`..`.8` are 256x256 BC5 sprites: the eight moon phases.
  `SystemTextures.ExtractSkyBodyTextures` pulls them into `_system_textures`
  (`--extract-sky-bodies`). **`Etc_Moon_A_Alb.5` is authored in TXTG format `0x107`** (an ASTC
  variant `TxtgTexture`'s table does not carry) and throws - the extraction isolates every texture
  behind its own try/catch for exactly this, because it runs from `ViewportPanel`'s constructor and
  an escaping exception there costs the whole viewport over one moon phase. `SkyBodyPass` walks to
  the nearest phase it does have.
  Drawn as a FULLSCREEN pass, not billboard geometry: each pixel rebuilds its view ray and is
  placed into the body's own tangent frame, which sidesteps quad orientation, projection and
  near/far entirely and behaves identically at any FOV. Runs after the sky, before the cloud dome.
- **Lens flare is the game's real `flare_filter_flare`** (`agl_technique_pfx.sharcb`, extracted by
  `--extract-lensflare-shaders` at `GHOST_NUM=4 IS_HALO=1 IS_DISTORTION=0`). Its whole interface is
  one sampler and a 192-byte `RegisterUBO`, so the block was recovered by reading the decompiled
  math, no capture needed:
  `ghost = (0.5 - uv) * C[0].x`, then
  `out = (src(uv) + src(uv+g*2) + src(uv+g*4) + src(uv+g*6) + src(uv + normalize(g)*C[1].w*2) * C[1].xyz) * C[3].xyz`.
  So `C[0].x` = ghost spacing, `C[1].xyz/.w` = halo tint/radius, `C[3].xyz` = intensity. Stepping
  toward screen centre and past it lands on the light's reflection through the centre, which is
  where a real lens puts its ghosts - and is why `cSrc` MUST be a bright-pass, or ordinary geometry
  gets dragged into the ghosts. `LensFlarePass` owns that threshold rather than sharing
  `BloomPass`'s intermediates, and composites additively on the HDR buffer BEFORE exposure.
  **Not done: the glare streak chain.** `glare_filter_blur` is extracted
  (`agl_glare_filter_blur0`/`1`) but is a 2-tap separable blur meant to run ping-pong with offsets
  growing per `BLUR_LV`, seeded by `glare_filter_seed`'s own `Seed` block - a multi-pass chain with
  its own state, none of it decoded. That is the anamorphic streaks, not the ghosts.

Still located but unbuilt:

- **The shader's own sun disc.** `sky_postfx_sky`'s `RENDER_SUN=1` variant, ALREADY EXTRACTED and
  unused (`agl_sky_postfx_sky_sun`). Its math decodes to
  `sky + clamp(dot(viewDir, sunDir) * C[12].z - C[12].w, 0, 1) * (lut * transmittance * C[12].x)`.
  Every input exists: `C[12].x` is `render_sun_intensity` (parsed), the extra sampler is
  `cTexTransmittance` (`SkyPrecomputePass.Transmittance`, already baked and numerically verified),
  and the extra block is `SizeInfo` (already reproduced bit-exactly). **The one unknown is the CPU
  mapping from `render_sun_size`/`render_sun_lerp` onto `C[12].z`/`.w`** - a linear ramp on the
  cosine, so `z` is roughly `1/(1-cos(angularRadius))`. Note the variant relocates every block
  (Context 0->1, RenderInfo 1->2, SizeInfo@0) and swaps the samplers
  (`cTexTransmittance`->`fp_t_tcb_8`, `cTexBakedInscatter`->`fp_t_tcb_A`), the same per-stage
  collision `CloudDomePass` already corrects at link time. The capture used `RENDER_SUN=0`, so
  `C[12]`'s live values are NOT recoverable from it - a capture with the sun on screen would settle
  it outright.
- **Moon.** A real BFRES: `Model/Obj_Moon_A.Obj_Moon_A_01.bfres.mc`, with `Obj_Moon_A.anim`, plus
  **eight** phase textures `TexToGo/Etc_Moon_A_Alb.1..8.txtg`. It is an ordinary actor, so Marrow's
  existing `ModelPreparer.Prepare` path can load it as-is - the work is placement on the sky dome
  and picking the phase, not new asset plumbing. The blood moon is separate:
  `Dm_HiRezRedMoon_A.*.bfres.mc` + `HirezRedMoon_4RunTime_A_Alb.txtg`, driven by
  `Obj_RedMoon_A_01.root.asb`.
- **Flare occlusion.** The real game gates the flare on `effect_sun_occlusion_cs`
  (`AglShader.sharcb`, compute) plus `occlusion_query`/`occlusion_renderer`, so the flare fades as
  geometry covers the sun. Marrow's flare has no such gate - it keys purely off HDR brightness, so
  a sun behind a model still throws ghosts until the model actually darkens those pixels.
  `occluded_effect_lensflare` (`agl_technique.sharcb`) is the geometry-based flare that pairs with
  it.
- **Stars.** `star_render` and `star_field_render` (`agl_technique.sharcb`, and `star_render` again
  in `AglShader.sharcb`).

Discovery for all of the above is `--list-agl-programs <romfs> [filter] [--combos]`; `--combos`
prints samplers and blocks per macro combination, which is what makes "does this variant need
inputs I do not have" answerable before writing any code.

## The real sky shaders are extracted but NOT wired (`agl::pfx::Sky`)

`BackgroundPass` renders a **hand-written** Rayleigh+Mie raymarch - the last piece of invented
shading left in the sky path, and the leading suspect for the atmosphere reading too bright. The
real programs it stands in for are now extracted automatically from romfs
(`TestAglShader.ExtractSkyPostFxShaders` / `ModelPreparer.EnsureSkyShaders`, CLI
`--extract-sky-shaders`) into `_shaders/agl_sky_*` - 34 files - but nothing links them yet.

Full decode, resolved uniform mapping, and the per-macro evidence: **`docs/agl_sky_postfx.md`**.
The three things worth knowing before touching this:

- **TotK's sky is Bruneton precomputed scattering.** The per-frame pass is one 2D LUT lookup plus a
  lerp to a fog colour; all the physics is baked offline by a chain of ~9 programs (also extracted).
  On those, `LOCAL_STEP` is not a variant to pick - it names which iteration of the solve a program
  is, so every declared value is a separate required pass.
- **`cTexBakedInscatter` has no romfs file** - it's baked at runtime, so the chain has to be run.
  `SkyPrecomputePass` does that: the **transmittance stage is implemented and numerically verified**
  (clean monotonic [0,1] LUT, zero violations in 16384 samples), which validates the reconstructed
  `Config`/`SizeInfo` blocks. Both blocks are declared as opaque BLOBS by
  `agl::pfx::Sky::Sky::initialize`, so there is no name table to recover - the layouts are inferred
  from use and documented in the doc above. `Config` comes from real ROM data Marrow already parses
  (`SkyPostFx`, out of `master_field.baglsky`), so nothing is hardcoded.
  **`SizeInfo` is now fully recovered** from a real capture via the RenderDoc MCP (see below) and
  `BuildSizeInfo` reproduces all 36 floats bit-exactly (asserted at startup). The inscatter axes
  are NOT Bruneton's reference numbers - TotK uses `MU=32, MU_S=32, NU=8, R=16` - so guessing them
  would have produced a wrong-but-plausible LUT. What remains is writing the inscatter/irradiance/
  bake stages themselves; the inputs they need are no longer unknown.

## RenderDoc MCP (`C:\Users\dylan\repos\RenderDocMCP`)

Inspects `.rdc` captures directly - list draws, read constant buffers, dump textures - instead of
exporting by hand. Register with
`claude mcp add renderdoc -- python C:\Users\dylan\repos\RenderDocMCP\server.py`. No dependencies.

The tool that matters for this project is **`renderdoc_find_draws`** (match on constant-block names
+ texture count) and **`renderdoc_search_constants`** (match on buffer CONTENTS). Both exist because
in a Ryujinx capture every draw is named `vkCmdDrawIndexed()` and the shaders were generated from
Tegra ISA, so there is no program name to grep - but the generated block names (`fp_c3`, `fp_c11`…)
are the SAME ones in our offline-decompiled GLSL, which makes a program identifiable by the shape of
its interface. Note a block's **bound size is not its struct size** (the emulator rounds bindings up
to 256 bytes and declares every block `data[4096]`), so identify blocks by contents, never by size.
RenderDoc's Python API is also unstable across versions and ships no stubs - use
`renderdoc_introspect` rather than guessing a method name.
- **`fp_c3.data[13].x` is the sky's single scalar brightness knob** (`Context`, last vec4). Relevant
  if the over-brightness turns out to be reproducible in the real shader too.

Use `--list-agl-programs <romfs> [filter] [--combos]` to discover programs before hardcoding a name
or macro combo in a new extractor - `--combos` prints samplers/blocks per macro combination, which
is what makes a macro choice evidence-based rather than the admitted guess
`ExtractCloudNoiseShader`'s `RENDER_TYPE` still is.

## The sky's colour comes from FogColor's HUE, not BgDifColor

Chasing "the sky is orange, not red" through slider after slider was wasted effort: **BgDifColor
IS orange**. BloodyMoon's is `(1.0, 0.297, 0.214)` = `#FF4C37`, saturation 0.79 with green at 30%,
and no tint toward an orange can produce red. Its `FogColor` is `(0.035, 0, 0.008)` - `#FF0039`
normalised, green exactly ZERO, saturation 1.0. That is the blood-moon red, and it follows from the
palette: with `rayleigh 0` and `mie 256` this atmosphere IS dense fog, so the fog colour is the sky
colour. The tint uses the fog HUE (normalised to its brightest channel); brightness stays with
`BgDifIntensity` and the Sky HDR Level.

`SpectralCalibration` is now NEUTRAL. It was `(1/1.166, 1, 1/0.770)` - reciprocals of a real
measured mismatch against the captured inscatter table, but measured on exactly ONE palette (a noon
blue sky) and then applied to all of them, where it cuts red 14% and boosts blue 30% and drags a
saturated red from 0.79 saturation to 0.68. Compensating for an unexplained residual with a
constant measured on one sample is how a fudge becomes a bug.

## The default exposure (2.5) is a stand-in, not a derivation

The real game authors `Exposure: 1.0`. The renderer's default was 9.5 for a long time, calibrated
when `cTex_DeferredLightPrePass` was **identically zero** - see `LightPrePass`, which names that as
the likely dominant reason for it. That buffer is no longer zero, and the default
(`LightingContext.Exposure`) is now **2.5**, chosen by hand. Comments elsewhere that say
"~9.5x" describe the old default, not a constant anything depends on - every pass reads the live
value.

`ExposureMeter` measures what the scene actually needs rather than guessing: it reads the HDR buffer
before exposure and takes the GEOMETRIC mean of luminance (the bulk of the image, not its brightest
pixels - an arithmetic mean is dominated by a sun disc), excluding near-black so an empty viewport
cannot set the level. "Measure scene exposure" in the Inspector reports it. It deliberately does not
APPLY the value: what remains after the light pre-pass are ~144 real per-region local lights
(`envobj/master_field.baglenv`) and runtime reflection-probe IBL (`envobj/common.baglcube`), so a
gap is expected and the number is evidence, not a verdict.

## The tonemap chain, and the one step that was Marrow's own

`Exposure` -> highlight compression -> `agl_hdr_compose`. Only the middle step is Marrow's; the
compose is the game's real decompiled program and its `cParam = (1, 0)` is the IDENTITY, i.e. it
applies no highlight desaturation at all. So when saturated colour comes out grey, `hdr_compose` is
not the suspect - the compression before it is.

That compression used to run PER CHANNEL, which desaturates by construction: for a saturated colour
only the dominant channel exceeds the knee, so only it is pulled down while the others pass through
and the three converge toward grey. Measured on a blood-moon red `(3.0, 0.6, 0.45)`: saturation
0.850 -> 0.772 at that step alone. It now compresses on the brightest channel and scales the colour
as a whole, preserving every channel ratio; neutral colours are unaffected either way.

**Treat values above 1 as normal, not as a bug.** TotK is an HDR renderer and this chain exists to
bring HDR into display range. Anchoring the sky/clouds below 1 "to avoid clipping" produced a
technically-unclipped, visibly flat image - the failure mode that chased this for several rounds.
`DeferredPipeline.SkyHdrTargetPeak` aims the sky ABOVE 1 deliberately.

**Not implemented: `agl::pfx::ColorCorrection`.** `postfx/master_field.baglccr` is real authored
data Marrow never reads, and it carries `saturation = 1.175` (plus hue/brightness/gamma and a set of
level curves). The game's final image is meaningfully more saturated than its raw render because of
it, so anything comparing Marrow to a screenshot is comparing against a post-processed image.

## Material-parameter usage detection (which `gsys_material` fields does this shape's shader actually read)

`ExportManifest.cs`'s `FindUsedParamNames`/`IsReferenced` (called right after decompiling a shape's
programs) works out, per material, which of its own typed parameters are referenced by ANY of its
resolved G-buffer/Z-only/forward shader text, and re-writes that material's `params.json` with a
`"used": true/false` field per typed uniform (`MaterialUniformEntry.Used` on the runtime side).
`MaterialInspectorPanel` hides a parameter whose `Used` is explicitly `false`; `null` (older cache,
predating this feature, or the `fp_c11` naming assumption not matching any text at all) is treated
as "show it" - the safe default. This is text-pattern matching against the real decompiled GLSL, not
guessing: it looks for `fp_c11.data[SLOT]` bare (whole vec4 - always counts as used) or followed by
a swizzle naming the parameter's own byte(s). Parameters bigger than one vec4 (matrices, `Srt2D`/
`Srt3D`/`TexSrt`/`TexSrtEx`) are always treated as used - not pattern-matched, since wrongly hiding
a genuinely-used one is worse than never hiding one of these. Re-run `--prepare` on a model prepared
before this feature existed to get real values (older caches have no `"used"` field at all).

## The "entire app flickers" bug - narrowed to a sustained GPU-cost regression, partially mitigated, not eliminated

Precise symptom as most recently described by the user: not a crash, not visible in a screenshot at
all - a brief flash where the whole UI goes solid or ~50%-opacity BLUE, happening rarely at first
but reliably and repeatedly after the window is fullscreened once. No exception ever showed up in
the console log while this was captured live (`Program.OnRender`'s per-panel `try`/`catch` logs any
panel that throws - see `ReportRenderFailure` - and nothing fired), which rules out a C# exception
unbalancing ImGui's window stack as the mechanism. An earlier theory chain (unbalanced `Begin`/`End`
pairs, a leftover offscreen FBO not rebound before `_imgui.Render()`) was checked directly against
`Program.cs`'s actual code and doesn't hold up - `ViewportPanel`'s `Begin`/`End` is already
try/finally-guarded, and FBO 0 + the full viewport IS explicitly rebound right before
`_imgui.Render()`.

**Root cause (strong circumstantial case, not proven at the OS/driver level): cloth and helper
bones together turned an occasional full-pipeline render into a permanent, continuous, uncapped one.**
Before cloth existed, `ViewportPanel.RenderContentsIndented`'s `if (_state.Dirty)` block - which
runs the ENTIRE deferred pipeline (G-buffer, shadow, sky/cloud, bloom, tonemap, every post pass) -
only fired when something actually changed (camera orbit, a placed actor moving); the rest of the
time the viewport just redisplayed the cached `_state.LastFrame` texture, cheaply. Since cloth
shipped, `RenderActor.AdvanceAnimation` forces `Dirty = true` on literally every frame whenever
`ClothEnabled && ClothInstances.Count > 0`, and separately `RenderActor.EvaluatePosedSkeleton`
returns non-null bone matrices whenever `HelperBonesEnabled && Model.HelperBone != null` -
independent of the `ClothEnabled` toggle - which `DeferredPipeline`'s `anyAnimated` check turns
into a forced shadow-cache miss too. Since Zelda has helper bones on by default, this means: from
the moment cloth was wired in, the full expensive pipeline runs at full cost on literally every
rendered frame, forever, for as long as any cloth-or-helper-bone-capable actor is placed - a
sustained continuous GPU load this pipeline never had to sustain before. Every fullscreen-pass stage
(sky, clouds, bloom, tonemap, SSAO) scales with resolution, so fullscreening multiplies that
continuous cost - fitting "started after cloth, but only reliably triggers after the window goes
fullscreen" exactly. A frame that blows its time budget under DWM composition is a known way to get
an unscreenshottable flash of a placeholder colour, which fits "can't capture it" too. This explains
the correlation well; it does not, and cannot from static analysis alone, prove the exact OS/driver
mechanism behind the specific colour shown.

**First attempted fix (REVERTED - traded one bug for a worse one).** Throttled the shadow-cache miss
so an animated actor only forced a real shadow recompute every 4th frame instead of every frame,
reusing the stale shadow the other 3. This measurably reduced the blue-flash frequency (user
confirmed: "happens way less"), but introduced a directly visible regression: the shadow visibly
snaps/steps every time it does refresh instead of tracking smoothly, which reads as "flickers every
frame" during real animation - reported by the user immediately after landing it, and an obvious
consequence in hindsight (skipping 3 of 4 shadow updates while a character is actively animating
means the shadow silhouette visibly lags and pops). Reverted in full - see `DeferredPipeline.cs`'s
own remarks at the shadow-cache-hit check for why frame-skipping isn't the right lever here.

**Second fix (LANDED, real, zero visual cost, addresses the actual waste instead of skipping
frames).** The shadow pass had every shape draw with its full `GBufferProgram` - the real,
expensive, decompiled game fragment shader (every texture sample, full material/lighting math) -
into a depth-only target with no colour attachment, so all of that computed colour was thrown away
every single time. `GBufferPass.cs`'s own Z-prepass already solved exactly this problem for its
Z-only-then-EQUAL-test trick: a shape with a resolved `ZOnlyProgram`/`ZOnlyVao` is the real game's
own cheap depth-only shader variant (still correctly alpha-testing a cutout material, since it's the
literal same shader the game uses for its own depth prepass), and `ShadowPass.Run` now uses it too
whenever a shape has one, falling back to the full G-buffer program only for a shape that doesn't -
identical depth output, a fraction of the fragment cost, every frame this pass runs. This is a
genuine, unconditional win regardless of the flicker investigation (the shadow pass was always doing
this waste, just rarely enough pre-cloth not to matter) and directly attacks the actual continuous
per-frame GPU cost cloth/helper bones now force, without the frame-skipping approach's visible
side effect.

**Status: reduced, not eliminated - the user still wants it gone entirely.** Not yet re-tested live
against the current combination (Z-only shadow fix landed, throttle reverted). If it still recurs,
the next lever is reducing OTHER passes' continuous per-frame cost the same way (real work, not
skipped frames) - candidates not yet investigated in this pass: whether `BloomPass`'s downsample
chain, `ScreenSpaceShadowAndAoPass`'s AO/PCF taps, or the sky/cloud passes carry similar "always
computes more than the visible result needs" waste. Do NOT reach for frame-skipping/throttling
`Dirty` or any per-frame recompute again without confirming the skipped work is genuinely
imperceptible over the FULL skip interval under REAL animation, not just cloth's own subtle
jiggle - the shadow throttle attempt is the concrete cautionary example.

## Known issues (state as of this session - re-verify before trusting)

- **Enemy_Drake / Enemy_MiasmaTentacle eye colour (reported: blue/wrong instead of orange).**
  RULED OUT: the shared iris texture (`Cmn_Enemy_DungeonBoss_Eye_Alb`, BC5) is confirmed correct -
  it independently decodes (Switch Toolbox) to the same green/black/red content Marrow itself
  renders it as, so this is **not** a texture-format or channel-swizzle bug (the BC5 blue-swizzle
  fix above is real and worth keeping, but doesn't fix this). The remaining bug is in the real
  decompiled shader's colour math or in what Marrow feeds it (a `gsys_material` const-colour tint,
  a blend against a second texture, a LUT-style remap) that's supposed to turn that raw
  green/black/red mask into the game's actual orange iris - not yet located. Re-read
  `material_prog11146_extracted.frag`'s (or whatever program the current manifest names for
  `Center_Eye_2__Mt_Eye`/`Iris_Model__Mt_Eye`) full `out_attr1` computation against every
  `fp_c11.data[N]` it touches, by name, before touching texture code again.
- **Enemy_Giant eye too bright.** `DeferredResolvePass.cs`'s remarks on `Run`'s `exposure`
  parameter already document and fix ONE real cause (emission authored in the game's units getting
  multiplied by Marrow's 10x lit-path exposure correction, blowing past the highlight-compression
  ceiling) specifically citing this creature's eye by name. If it's still too bright after that
  fix, the remaining cause is unconfirmed - candidates include the glossy (`chara_grossy`)
  specular response having no environment/IBL term to compete against (Marrow doesn't implement
  cubemap IBL), or the icon-capture path's separate hardcoded exposure
  (`IconCapturePreset.cs`) not routing through the same fix.
- **Enemy_Dragon_Darkness missing pupil/iris.** `Mt_Eye_L`/`Mt_Eye_R` render as alpha-cutout
  ("mask" render state) against `gsys_alpha_test_ref_value = 0.5`, discarding on the shared iris
  texture's own alpha. Not yet confirmed whether the shape is dropped entirely (no G-buffer program
  resolved - `ModelLoader.cs`'s silent-skip path), discarded near-100% by the alpha test, or
  something else. There's also a documented, separate texture-SRT animation
  (`Face_Eye_Scroll_fts` -> `p_tex_srt1`, see `BuildMaterialUbo.cs`'s `WriteParamLayout` remarks)
  that scrolls this exact material's eye texture - worth checking whether its BASELINE (unanimated)
  `p_tex_srt1` value is a sane "neutral" UV transform.
- **Npc_Zelda hair colour (purple, should be blonde) - FIXED**, confirmed by the user in the real
  app: this was the same BC5 blue-swizzle bug as Drake/MiasmaTentacle's eyes (see above). No
  further action needed here.
- **Npc_Zelda_AncientHyrule's default eyes (dragon instead of human)** - still open, not
  investigated. The material parameter editor below should make this faster to diagnose
  interactively (inspect `p_const_color*` on the eye material live) - but note it can't fix a wrong
  SHADER VARIANT (which eye texture/branch gets compiled in), only wrong numeric/colour values
  within whichever variant is already baked in; if this turns out to be a variant-resolution
  problem (see "Key concepts" above on `TestTOTK.GetShaderProgram`), it needs a different fix
  entirely.

- **Material parameter editor**: built, directly in `Marrow.UI/Windows/MaterialInspectorPanel.cs`
  (extends the existing read-only inspector rather than a new panel). Edits every `gsys_material`
  parameter the selected shape's material authors a value for (colour/scalar/vector/bool/int -
  matrices, SRTs and texture-SRTs aren't supported yet, no dedicated widget for those). Requires
  `<Material>.params.json` regenerated with the new `"type"` field
  (`BuildMaterialUbo.WriteParamLayout`) - re-run `--prepare`/`--rebuild-matubo` for a model prepared
  before this change, or its materials show no editable parameters. Compiles clean; not yet
  exercised in the running app - see `tasks_set1.md` for the full writeup.

## Working agreements for this project specifically

- **Ask before a large investigative detour**, especially anything that touches rendering output
  or builds new tooling to "see" something. Static analysis (manifest + real shader + raw texture
  bytes) goes a long way and is much cheaper to trust than a rendered image from a harness nobody
  has validated yet.
- When a fix changes what should appear on screen, the user verifies it by running the real app -
  don't claim a visual bug is fixed based on your own tooling's render.
- `git status`/`git log` and this file drift; if `Known Issues` above disagrees with what you find
  in the code, trust the code and update this section.
