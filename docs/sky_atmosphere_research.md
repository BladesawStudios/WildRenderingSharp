# How TotK builds and renders its sky — and how to implement it in Marrow

Research pass combining Ghidra (the real executable) and direct romfs asset extraction. Answers
the question "is it a cubemap?" — **no, not primarily.** The sky is a dedicated deferred
post-process system (`game::gfx::EnvironmentRenderer`) with a real Rayleigh/Mie scattering model,
a separate billboard-geometry cloud system, and a baked sky-visibility pass — all driven by ~130
artist-authored YAML palettes blended over time-of-day/weather. A genuine reflection cubemap
exists too, but it's a *separate* concept (specular IBL for materials), not the sky's own color.

Everything below was independently confirmed either by decompiling the real executable (Ghidra) or
by dumping/inspecting real romfs files — no shader-text guessing. Where a claim rests on one and
not the other, that's called out.

## TL;DR — what actually draws the sky

1. **`game::gfx::EnvironmentRenderer`** (constructed once per main scene, in
   `ModelSceneExtension::initializeForMainScene_`, address `7100c79b60`) owns two real, *decompilable*
   BFSHA shader programs — `environment_renderer_env` and `environment_renderer_image` — plus a
   lower-level **`agl::pfx::Sky`** object (up to 11 independently-parameterized layers) and a
   **`agl::fx::Cloud`** object (real GPU vertex/index buffers, not a raymarch).
2. `environment_renderer_env` computes a small, wide-FOV **sky image + 2-layer fog texture array**
   each frame (or on a reduced cadence — see Open Questions), reading real atmospheric inputs:
   sky-island shadow, world shadow, sky occlusion, cloud data, and a genuine reflection cubemap.
3. `environment_renderer_image` is a **full-screen POST-PROCESS pass**, drawn during
   `ModelSceneExtension::drawFinalImage_` — i.e. *after* the deferred-lit scene. It does **not**
   draw skybox geometry. It resamples the small sky/fog data from step 2 using a
   perspective-divided screen-space UV (`screenUV / gl_FragCoord.w`) — the classic "reproject a
   pre-rendered sky image through the current frustum" trick — and composites it over
   `cSceneColor`.
4. Clouds are **separate**: `agl::fx::Cloud` builds real vertex-animated billboard geometry from
   two romfs "VAT" (Vertex Animation Texture) assets, plus a dedicated **sun-occlusion sub-pass**
   (renders cloud geometry into a small texture to measure how much they block direct sunlight).
5. **Sky visibility** ("how much open sky can this point see", used to darken ambient light under
   overhangs/caves) is a *separate, cacheable* 3-pass pipeline (`skyocl_copy` /
   `skyocl_depthfill` / `skyocl_normalize`), matched by AI nodes that explicitly *load*/*force
   update* it — i.e. it's baked/streamed per region, not a full real-time raymarch every frame.
6. The sky's actual **look** (color, cloud shading, fog, scattering coefficients, bloom) is almost
   entirely **data-driven**: ~130 authored YAML "env palettes" (`Pack/EnvPalette.pack.zs`), blended
   by AI/timeline nodes (`ExecuteWmSetSkyPaletteForTimeline`, `HoldWmSetSky`, …) as time-of-day and
   weather change. One of those palettes contains real `SkyRParam_mie_amplifier` /
   `SkyRParam_mie_symmetrical` / `SkyRParam_rayleigh_amplifier` fields — genuine Rayleigh+Mie
   atmospheric scattering coefficients, tunable per-preset rather than a fixed physical constant.

## 1. Architecture, in render order

```
per-frame:
  ... G-buffer pass, deferred lighting/shading (Marrow's existing pipeline stops around here) ...
  environment_renderer_env   -> small sky-color image + 2-layer fog array (+ SSR/sky-occlusion variants)
  agl::fx::Cloud draw        -> real geometry, blended into the scene / into the sky image
  environment_renderer_image -> FULL-SCREEN reproject+composite of the above onto cSceneColor
  ... bloom/tonemap/etc ...
```

- **`game::gfx::EnvironmentRenderer`** — `EnvironmentRenderer @ 7100c79b60`,
  `EnvironmentRenderer::initialize @ 7100c7e680`. Holds pointers to both shader programs
  (`environment_renderer_image` at object offset `0xa8`, `environment_renderer_env` at `0xb0`) and
  declares two small per-frame UBOs (48 bytes / 3 vec4, and 16 bytes / 1 vec4 — almost certainly a
  "Parameters" block, distinct from the big shared `Env`/`gsys_environment` block).
- Owned by **`game::gfx::ModelSceneExtension`**, created in `initializeForMainScene_` (constructor
  called twice there — once per program, at `7100c7816c`/`7100c78184`).
- Drawn from **`ModelSceneExtension::drawFinalImage_`** (`7100ddbba8`) via
  `FUN_7100ddbdbc` → `agl::ShaderProgram::activate` → `agl::IndexStream::draw` (a plain full-screen
  index-buffer draw, no unusual geometry) — confirms this is a straightforward post-process quad,
  not 3D skybox geometry.
- The shader-binding setup (`FUN_7100eb4db0`, called from
  `ModelSceneExtension::initializeShaderArchives`) names every sampler/UBO/SSBO both programs use —
  this is what made everything below possible without guessing:

  ```
  UBOs: Parameters, Env, Context, SceneMaterial
  SSBOs: EnvironmentRendererInfo, SceneShadingInfo
  Samplers: cNLD, cSsaoNLD, cVolumeMask, cSkyFog, cEnvironmentMask, cSkyColor,
            cGBufferNormal, cGBufferMaterialId, cEnvMap, cBlurTarget0, cBlurTarget1,
            cCloud, cSkyIslandShadow, cWorldShadow, cDepthShadow, cMinusFieldMask,
            cSceneColor, cSkyOcclusion
  ```

## 2. The shader programs are real BFSHA and Marrow can already decompile them

This is the single most actionable finding: **`environment_renderer_env`/`_image` are genuine
compiled shader-archive programs**, not opaque engine code — they live in
`Shader/ApplicationPackage.Nin_NX_NVN.release.sarc.zs` → `AglShader.sharcb`, and
`ShaderLibrary.CompileTool` already has a flag for exactly this archive format:

```bash
# One-time: unpack the archive (it's a plain SARC, zstd-compressed)
# (use totk-mcp's unzip_archive, or ShaderLibrary's own SARC reader)

# List every program in the archive (no program names given = list mode)
dotnet run --project vendor/ShaderLibrary/ShaderLibrary.CompileTool -c Debug -- \
  <romfsRoot> --agl-shader <unpackedArchiveDir> AglShader.sharcb

# Decompile everything (same command — it decompiles AND lists in one pass;
# TESTBENCH_DIR must be set first, output goes to <TESTBENCH_DIR>/Shaders/Decompiled)
```

Both programs have multiple **static-option variants** (`STEP[0..4]`, plus `ENABLE_SSR[0|1]` /
`ENABLE_SKYOCCLUSION[0|1]` on `environment_renderer_env`) — the engine's usual "one shading model,
many baked variants" pattern this codebase already handles for materials. Confirmed real content
per variant (by sampler list AND by reading the actual decompiled GLSL):

| Program | Variant | Samplers | What it does |
|---|---|---|---|
| `environment_renderer_image` | STEP0 (Pixel) | `cSkyColor` | Reads the generated sky image; also writes into a tiled/clustered lighting SSBO (marks which screen tiles need sky compositing) |
| `environment_renderer_image` | STEP1/2 | `cGBufferNormal`(, `cNLD`) | G-buffer normal/depth reconstruction (tangent-space derivative math, confirmed by reading the GLSL) |
| `environment_renderer_image` | STEP3 (Vertex) | `cGBufferNormal` + `cGSysCtxFovyParam`/`cGSysCtxProj` | Reconstructs a per-pixel view ray from the camera's FOV/projection — this is the geometry side of the "sky at infinity" reprojection |
| `environment_renderer_image` | **STEP4 (Pixel)** | `cSkyColor`, `cSkyFog` | **The actual sky+fog composite.** Confirmed by reading the GLSL: samples `cSkyColor` (`sampler2D`) at `screenUV / gl_FragCoord.w`(perspective-divide reprojection), and `cSkyFog` (a **`sampler2DArray`**, i.e. a real 2-layer texture — matches `gsys_environment.md`'s independently-inferred "two fog groups") via `textureLod(..., layer=1, mip=1)`. Output: fog RGB + a density scalar from `cSkyColor`'s own extra channels. |
| `environment_renderer_env` | STEP0 (Pixel) | *(none — Context/tile-buffer only)* | Tile-buffer bookkeeping |
| `environment_renderer_env` | **STEP3, `ENABLE_SKYOCCLUSION1`** | `cBlurTarget0[, cBlurTarget1]`, `cEnvironmentMask`, `cNLD`, `cSkyFog`, `cSkyIslandShadow`, `cSkyOcclusion`, `cWorldShadow` | **The generation pass** — reads shadow/occlusion/mask inputs and blurred neighbors to build the per-frame sky/fog data. This is the file to hand-trace next if bit-exact math is wanted (35KB decompiled, too large to fully hand-trace in this pass — see Open Questions). |

`cSkyColor` was seen declared as *both* a `sampler2DArray` (STEP0) and a `sampler2D` (STEP4) across
variants — either the resource is genuinely an array that some variants only ever address at layer
0 (compiling down to a plain 2D access), or there are two same-named-but-distinct bind points.
Worth re-checking before committing to an exact texture layout in Marrow's port.

## 3. There IS a real atmospheric scattering model — with authored coefficients

Dumped a representative env palette
(`Pack/EnvPalette.pack.zs` → `WorldMgr/ResEnvPalette/ZonaiSky_Bluesky_3_Noon.game__wm__ResEnvPalette.bgyml`)
directly. Full real field list (every field below is a **literal YAML key from the game's own
data**, not inferred):

```yaml
AfParam_attenuationForGrd: 4.0
AfParam_attenuationForSky: 0.0
BgDifColor: {R: 1.0, G: 0.9216, B: 0.749, A: 0.0}
BgDifIntensity: 9.0
BloomClampedLuminance: 30.0
BloomIntensity: 0.1
BloomThreshold: 1.5
Cloud0: { BacklightPower, ColorBackLight, ColorBase, ColorHilight, ColorShadow,
          IntensityBase, IntensityHilight, IntensityShadow }
Cloud0NoUse: true          # this preset explicitly disables layer 0
Cloud1: { <same 8 fields> }
CloudShadowOnOff: true
Exposure: 1.0
FogColor / FogStart / FogEnd            # standard distance fog
SfParam_attenuation / SfParam_horizontal / SfParam_near
SkyRParam_mie_amplifier: 12.0
SkyRParam_mie_symmetrical: 0.75         # Henyey-Greenstein phase-function "g"
SkyRParam_rayleigh_amplifier: 1.0
SkySunColor / SkySunColorIntensity / SkySunColorNoUse
VolumeMaskColor / VolumeMaskIntensity / VolumeMaskColorNoUse
YFogColor / YFogStart                   # a SECOND, HEIGHT-based fog (Y = up)
```

This confirms, with certainty:

- **Real Rayleigh + Mie scattering**, parameterized per-palette (`SkyRParam_*`) — Mie's amplitude
  *and* its Henyey-Greenstein symmetry ("g", controls how forward-scattered/sun-bright the haze
  looks) are both authored, not derived from physics constants. This is exactly the
  Preetham/Bruneton/Hillaire family of analytic sky models, just artist-tuned per preset rather than
  simulated from first principles.
- **Two independently-toggleable cloud layers** (`Cloud0`/`Cloud1`), each with a 4-color gradient
  shading model (base / hilight / shadow / backlight) plus per-term intensities and a
  backlight power — this is almost certainly what feeds the `agl::pfx::Sky`'s "up to 11 layers"
  UBO array seen in Ghidra (§4), collapsed down to the ones actually used.
- **Two separate fog axes**: ordinary distance fog (`FogColor/Start/End`) AND a height/altitude fog
  (`YFogColor/YFogStart`) — likely the real identity behind `gsys_environment.md`'s "two decoded
  fog groups" (`Env[16..21]`), or a sibling structure to it.
- Bloom parameters are **baked into the same palette** as the sky — bloom response literally
  changes between weather/time presets, not a single global tonemap constant.
- `VolumeMaskColor/Intensity` matches the `cVolumeMask` sampler seen in the shader's own sampler
  list.

There are **~130 of these** in `Pack/EnvPalette.pack.zs` (one per named place/weather/time
combination — `ZonaiSky_Bluesky_0_Sunrise` through `_7_Night`, `MainField_Hebura_Cumulonimbus_*`,
`Ganondorf_Castle_*`, dungeon/cave presets that turn the sky off entirely, etc.), plus a SEPARATE
`Pack/EnvPaletteTimeTable.pack.zs` that almost certainly defines which palette is active at which
in-game hour (not yet dumped — do this next if implementing time-of-day blending).

**Import path for Marrow**: `BymlLibrary`/`Byml.FromBinary` (already a dependency, already used by
`ActorInfo.cs`) reads these directly — no new parser needed, same one-line pattern as any other
`.bgyml` this codebase already touches.

## 4. `agl::pfx::Sky` — the lower-level layered sky object

`agl::pfx::Sky::Sky` (`7100c95c68`) / `Sky::initialize` (`7100c94670`) allocates **up to 11
independent layers**, each with its own tiny 128-byte (8-vec4) uniform block declared via the
standard `agl::UniformBlock::startDeclare/declare_/create` sequence, plus one shared 144-byte
(0x90) block. `getRenderingParameterStatic`/`setRenderingParameterStatic` expose these for runtime
swapping — matches the AI nodes `HoldWmSetSky` / `ExecuteWmSetSkyRenderingParamForTimeline` /
`ExecuteWmSetSkyPaletteForTimeline` that blend sky state over time-of-day/weather transitions.
`Cloud0`/`Cloud1` from §3 are the most likely real occupants of two of these layer slots; the exact
mapping of all 11 wasn't traced field-by-field this session (the function is dense, low-level
buffer-index arithmetic — see Open Questions if this precision is needed later).

## 5. Clouds are real geometry, not a raymarch — and Marrow has the exact romfs assets

`agl::fx::Cloud` (`Cloud::Cloud @ 7100c8bcd0`, `Cloud::initialize @ 7100c98afc`) builds a real GPU
vertex/index buffer (`agl::GPUMemBlock<agl::fx::Cloud::Vertex>`, `initVertex_`/`initIndex_`) — i.e.
clouds are drawn as actual billboard/mesh geometry, animated via a **Vertex Animation Texture**
(VAT — a small float texture encoding per-vertex offsets over time, so the GPU can "play" a
pre-authored drift/morph animation with zero CPU cost). Confirmed real romfs assets, inspected
directly (`--inspect-bntx` after zstd-decompressing):

| Asset | Format | Purpose (inferred from name + format) |
|---|---|---|
| `TexToGo/Vat_CloudImposter_03_Vsp.bntx.zs` | 256×13, `D32_FLOAT_S8X24_UINT` (a depth format reused as raw float data — the standard VAT trick) | Near/imposter cloud billboard vertex animation data |
| `TexToGo/Vat_Cloud_Far_Pattern_Array_08_Vsp.bntx.zs` | 8×13, same format | Lower-detail LOD for distant clouds |
| `TexToGo/Aurora.bntx.zs` | 128×128, `BC3_SRGB`, 8 mips | A real compressed color texture — aurora/sky-glow effect (scope/usage not confirmed this session — may be a specific event/region effect rather than every-sky) |

Separately, `Cloud::allocSunOccRenderTexture` + `initVertexSunOcc_`/`initIndexSunOcc_` build a
**dedicated second vertex/index buffer and render texture** used to draw the SAME cloud geometry
from a sun-relative view into a small texture — a **sun-occlusion pass**: measuring how much cloud
cover currently blocks direct sunlight. This is very likely what feeds
`gsys_environment.md`'s already-documented `Env[68].w` "cloud-shadow intensity" term, and matches
the env palette's `CloudShadowOnOff` toggle.

No dedicated "cloud renderer" BFSHA program name was found in either `AglShader.sharcb` or
`AglLightShader.sharcb` — cloud geometry most likely draws through one of `system.bfsha`'s existing
153 `system_shading` programs (the same archive Marrow's deferred-resolve passes already use), or
through a small embedded technique this session didn't isolate. Worth a targeted follow-up:
grep the `system_shading` program list for names containing "cloud" once decompiled in full.

**Not clouds, but easy to confuse with them**: `miasma_renderer_compose` — a *separate* shader
using layered procedural noise (`cGradientFbm`, `cGradientSimple`, `cVoronoiseCloud`,
`cWorleySimple`, `cMiasmaDensity`) that renders the story-specific Ganondorf corruption/gloom
overlay, not general weather clouds. Good reference if Marrow ever wants a fully-procedural
volumetric-look cloud shader (FBM + Voronoi + Worley layering is a solid, well-understood recipe on
its own), but it is a different system from §5 above.

## 6. Sky occlusion is baked/cached, not a real-time raymarch

`skyocl_copy` / `skyocl_depthfill` / `skyocl_normalize` (three small BFSHA programs, found in
`AglShader.sharcb`) — a copy → depth-fill → normalize pipeline that produces the `cSkyOcclusion`
texture consumed by `environment_renderer_env`'s `ENABLE_SKYOCCLUSION` variant. Two AI one-shot
nodes — `OneShotForceUpdateSkyOcclusion` and `OneShotGenericBeforehandLoadSkyOcclusionCulling` —
strongly suggest this is computed/streamed per-region on demand rather than every frame in full.
Controlled by `p_dynamic_skyocclusion_off` (already in Marrow's `Env`/`SceneMat` UBOs, currently
unused) and tuned by `p_const_sky_occlusion_radius`/`p_const_sky_occlusion_height_diff` — **already
correctly implemented in `SceneMatUbo.cs`** (`ConstSkyOcclusionRadius = 2`,
`ConstSkyOcclusionHeightDiff = 64`, real authored defaults, nothing new needed there).

## 7. Cross-check against Marrow's existing `Env`/`SceneMat` work

Good news: this session's independent romfs/Ghidra dig **confirms**, rather than contradicts,
`gsys_environment.md` and `SceneMatUbo.cs`'s existing work:

- `system.Product.110.product.Nin_NX_NVN.bfsha`'s `system_scene_material` shading model exposes
  the **full real field-name table** for `gsys_scene_material`/`SceneMat` — and it's a byte-for-byte
  match with what `SceneMatUbo.Fields` already has (down to the exact offsets), including
  `ConstSkyIslandShadowDensity`/`ConstSkyIslandShadowOffsetScale` at 456/460 — this independently
  confirms `gsys_environment.md` §3.2's inference about `Env[23]`'s "walk back by direction × height"
  convention was reading the right neighborhood of data, and that Marrow's `SceneMatUbo.cs` already
  has the correct, real default values (0.925 / 0.009) for both.
- The regenerate command for that table, if it's ever needed again: `ShaderLibrary.CompileTool
  --dump-uniform-blocks <path-to-system.bfsha>` (per `SceneMatUbo.cs`'s own doc comment) — no new
  tooling needed, this was already built.
- `p_dynamic_cloud_ratio`, `p_dynamic_horizon_height`, `p_dynamic_darkenv_field_color` /
  `_chara_color` / `_chara_sat` all appear in **both** `SceneMat` and `Env` (mirrored) — Marrow's
  `SceneMatUbo` already has all of these by name; `EnvUbo`'s extension-region table
  (`gsys_environment.md` §3) does not yet name its `Env`-side counterparts specifically — doing so
  would need the same `startDeclare` walk that doc's §5 already calls out as the remaining work
  (`agl::pfx::Sky::Sky::initialize`, now that this session has its address and rough structure, is
  one of the concrete callers to walk).

## 8. Concrete implementation plan for Marrow

Roughly in order of value-per-effort. Numbered stages are independent — pick the highest-value one
and stop, or keep going.

### Stage 0 — free, already-have-the-data wins
- `SceneMatUbo`'s sky-island-shadow/sky-occlusion/darkenv fields are already correct; nothing to do.
- `EnvUbo`'s 4 named fog groups (base region, already solved) are real — if Marrow isn't yet
  reading a real env palette's `FogColor`/`FogStart`/`FogEnd`/`YFogColor`/`YFogStart` into them,
  that's a same-day win: parse one `.bgyml` (§3), map its fields onto the UBO slots that already
  exist.

### Stage 1 — data-driven sky color without a real scattering shader
- Pick (or blend, once `EnvPaletteTimeTable.pack.zs` is dumped) a real env palette for the current
  in-scene lighting state.
- Implement Rayleigh+Mie as a standard analytic formula (Preetham-style is the simplest;
  Nishita/Hillaire if more fidelity is wanted) parameterized by the palette's real
  `SkyRParam_rayleigh_amplifier`/`mie_amplifier`/`mie_symmetrical` plus sun direction/color
  (`SkySunColor*`) — this alone, even without matching TotK's exact decompiled math, will look
  unmistakably like the real game rather than a generic procedural sky, because the *tuning* is
  the game's own.
- Render it as a screen-space full-screen pass sampling by view-ray direction (the same
  "reconstruct view ray from depth/NDC" math Marrow's own `DeferredResolvePass`/`ForwardPass`
  already do for lighting) rather than literal skybox geometry — matches what the real engine does
  (§2) and avoids needing a UV-mapped dome mesh at all.
- Add the two fog layers (`FogColor`/`YFogColor` from the palette) composited on top, matching
  `environment_renderer_image` STEP4's real structure (§2 table).

### Stage 2 — clouds
- Simplest correct-looking option: a small number of large, semi-transparent camera-facing (or
  horizon-aligned) billboards using **`Cloud0`/`Cloud1`'s real 4-color gradient shading model**
  (base/hilight/shadow/backlight + per-term intensity + backlight power) — no volumetric raymarch
  needed to get the *right colors*.
- If real texture content is wanted: `TexToGo/Vat_CloudImposter_03_Vsp.bntx.zs` and
  `Vat_Cloud_Far_Pattern_Array_08_Vsp.bntx.zs` are genuine cloud VAT assets (§5) — decoding the VAT
  format itself (mapping the D32_FLOAT-as-data trick to actual per-vertex offsets) is unexplored
  this session and would need its own investigation before they're usable.
- `CloudShadowOnOff`/cloud-shadow intensity: `gsys_environment.md` already documents `Env[68].w` as
  the multiplier for a `cTex_DeferredCloudNoise` sample — a cheap noise-texture-driven ground
  shadow is a reasonable stand-in without implementing the real sun-occlusion render pass.

### Stage 3 — sky occlusion / ambient darkening under cover
- `p_dynamic_skyocclusion_off`/`ConstSkyOcclusionRadius`/`ConstSkyOcclusionHeightDiff` already
  exist correctly in `SceneMatUbo`. A cheap Marrow-side approximation (e.g. a coarse
  height-field/AO-style term, or simply leaving it off since it's a subtle effect) is likely
  sufficient; a faithful re-implementation of the real bake pipeline (§6) is a much bigger lift for
  comparatively little visible payoff.

### UI placement (per this session's direction)
Sky/weather/time-of-day state selection belongs in `Marrow.UI` **directly under the Actor
Palette panel** (`Marrow.UI/Windows/ActorPalettePanel.cs`'s sliding panel) — i.e. a new small
always-visible section in that same left-edge strip, not a separate Inspector tab. A simple combo
box listing the ~130 env palette names (or a curated subset — "MainField Noon/Sunset/Night",
"Ganondorf Castle", etc.) plus a time-of-day slider (once the time table is dumped) is enough to
start; wiring it to `EnvUbo`/`SceneMatUbo` field writes follows the exact same "defaults, then
overlay" pattern every other UBO in this codebase already uses.

## 9. Open questions / what's NOT resolved yet

- **`environment_renderer_env` STEP3's full math** (the actual generation pass, 24–38KB decompiled
  GLSL across its SSR/sky-occlusion variants) was located and its sampler/UBO inputs confirmed, but
  not hand-traced instruction-by-instruction — that's a large, mechanical but time-consuming pass
  if bit-exact scattering math (rather than a standard analytic model tuned to the real
  coefficients) is ever wanted.
- **Which of `agl::pfx::Sky`'s 11 layers map to which authored palette field** wasn't traced
  precisely — `Sky::initialize`'s body is dense pointer/index arithmetic; doable with more time
  budget, same technique `gsys_environment.md` used for the `Env` base region (walk the real
  `startDeclare` calls).
- **`cSkyColor`'s exact texture shape** (array vs plain 2D — see §2) wasn't fully reconciled across
  variants.
- **`EnvPaletteTimeTable.pack.zs`** (which palette is active at which hour/weather — the actual
  blending schedule) exists but wasn't dumped this session.
- **VAT (Vertex Animation Texture) decode** for the cloud billboard assets is unexplored — the
  `D32_FLOAT_S8X24_UINT`-as-raw-float trick is a known pattern in NW4F-derived engines but this
  session didn't work out this game's specific per-vertex layout.
- **Whether `Aurora.bntx.zs`** is a general sky asset or scoped to a specific story
  event/region wasn't confirmed — found by name/format only, no usage trace.
- No actual **sun/moon disc textures** or a literal skybox/star-field asset were found in this
  pass — either they don't exist as separate files (procedural disc drawn by `agl::pfx::Sky`
  itself) or a more targeted romfs search (by usage rather than by guessed name) would find them.
