# Forward-pass UBO map (working RE checklist)

**Status: my own working document, driven directly in Ghidra from here on** (not handed off to
Gemini this time - the user asked me to drive this one myself). Update this file in place as each
row gets resolved, citing the Ghidra function/address that confirmed it, the same way
`docs/gsys_environment.md` already does. Don't trust a row until it has a real citation.

## Why this exists

Two point-fixes (BC5 blue swizzle, `DynamicMonochromeSaturationForChara` default) landed real,
Ghidra-verified corrections but did **not** fix the reported bug: `ForceForwardOnOpaqueShapes`
still renders `Enemy_MiasmaTentacle`/a Ganondorf-like model bright uniform cyan, AND (newly found)
it removes the toon/ink outline ("sketch lines") on otherwise-correct models. Guessing at
individual slots one at a time isn't converging - this file maps out *everything* the forward
(`gsys_assign_material`) pass touches that Marrow doesn't yet have solid ground truth for, so the
remaining Ghidra work can be done systematically instead of by hunches.

## Scope: the two shaders analyzed so far

- `%AppData%\Marrow\cache\_shaders\material_prog10336_extracted.frag` - `Enemy_MiasmaTentacle`,
  `Mt_Skin` shape, forward program (the broken one).
- `%AppData%\Marrow\cache\_shaders\material_prog3248_extracted.frag` - `Enemy_Bokoblin`, `Mt_Skin`
  shape, forward program (a "should look right" model whose outline disappears).

Every UBO slot listed below was cross-checked against **both** files - the two are near-identical
in which UBO slots/textures they touch (see the per-UBO tables), strongly suggesting this is common
compiler-inlined "chara forward lighting" boilerplate shared across most/all forward programs, not
something specific to either creature. That's good news: fixing the shared unknowns should fix (or
at least change) the look on every affected model at once, not just these two. **Not yet confirmed
against a third/fourth forward program** - worth spot-checking once more, e.g. `Enemy_Dragon_Darkness`
or `Npc_Zelda_AncientHyrule`, to make sure this isn't a coincidence of these two specific materials.

Four UBOs total are declared by both shaders: `_Context` (`fp_c4`, binding 1), `_Mat` (`fp_c11`,
binding 8), `_Env` (`fp_c9`, binding 6), `_SceneMat` (`fp_c13`, binding 10).

---

## UBO 1: `_Context` / `fp_c4` (binding 1) - `src/WildRenderingSharp/Shaders/Profiles/Totk/Ubos/ContextUbo.cs`

Slots referenced by both forward programs, and their status per `ContextUbo.Slots`:

| Slot | `ContextUbo.Slots` name | Status | Notes |
|---|---|---|---|
| 0-2 | `View` | VERIFIED, populated | camera view matrix, fine |
| 11-13 | `ViewInv` | VERIFIED, populated | fine |
| 17 | `CameraParam3` | VERIFIED, populated | `(aspect*tanHalfFovY, tanHalfFovY, 0, 0)` |
| 20 | `Unknown10` (decl 10) | **UNCONFIRMED, zero** | read at frag lines 850/861 (Miasma) / 796/807 (Bokoblin) - not yet traced |
| 35-36 | `FrustumPlanes` (decl 15, part of a 6-row/slots 35-40 block) | **LABEL LIKELY WRONG** | see below |
| 37 | `FrustumPlanes` (same block) | **UNCONFIRMED, zero** | read at line 937/885 |
| 58 | `Unknown18` (decl 18) | **UNCONFIRMED, zero** | read at line 888/833 |

**Slot 35/36 finding**: `ContextUbo.cs`'s own doc comment labels slots 35-40 "FrustumPlanes" but
marks it `[DERIVED]` - inferred only from array shape (6 rows = 6 plane equations), never confirmed
against a real fill site. The forward shader's actual read pattern doesn't look like a frustum-plane
test at all - it's used as a row-vector transform feeding a 2D texture lookup into
`cTex_Projection0`:

```glsl
vec2(
    (fma(worldZ, fp_c4.data[35].z, fma(worldY, fp_c4.data[35].y, worldX * fp_c4.data[35].x)) + fp_c4.data[35].w) * scale,
    (fma(worldZ, fp_c4.data[36].z, fma(worldY, fp_c4.data[36].y, worldX * fp_c4.data[36].x)) + fp_c4.data[36].w) * scale
)
```

That's exactly the shape of two rows of a world-to-projective-texture-space matrix (U row, V row),
not a plane equation `dot(planeNormal, point) - distance` test (which wouldn't normally feed
straight into a 2D texture sample like this). **Priority #1 for Ghidra**: find where
`gsys::ShaderContext::createContextUBO`'s decl 15 actually gets filled at runtime (not just
declared) - it's very likely a "forward terrain/decal projection" matrix, not frustum culling data,
and Context is shared by every pass so getting this right could matter for Marrow generally, not
just the forward pass.

## UBO 2: `_Mat` / `fp_c11` (binding 8) - out of scope here

This is the per-shape `MaterialUbo`, already correctly built offline per-material
(`BuildMaterialUbo.BuildBlock`, name-joined from the shading model's compiled defaults + the
material's own `ShaderParams`). Not a target for this investigation - already solved by the
existing offline pipeline, verified working for every other pass.

## UBO 3: `_Env` / `fp_c9` (binding 6) - `src/WildRenderingSharp/Shaders/Profiles/Totk/Ubos/EnvUbo.cs`

Every slot either shader reads, cross-referenced against `EnvUbo.Slots`/`PowExponentSlots`:

| Slot | `EnvUbo` name | Status |
|---|---|---|
| 4, 5 | `LightDir0`, `LightColor0` | VERIFIED, populated |
| 16 | `WorldFogMaskColor` | VERIFIED-declared, but populated with an **arbitrary placeholder** `(0.55,0.62,0.72,0)` - see Finding 2 below |
| 17, 18 | `WorldFogMaskDir`, `WorldFogMaskStartEndInvDamp` | VERIFIED-declared, populated (fog disabled defaults) |
| 19 | `FogFxColor` | same placeholder-colour concern as slot 16 |
| 20, 21 | `FogFxDir`, `FogFxStartEndInvDamp` | VERIFIED-declared, populated (fog disabled defaults) |
| 23 | `LightDir0World` | VERIFIED, populated |
| 26-29 | *(extension, undocumented)* | **UNCONFIRMED, zero** |
| 30 | *(extension, undocumented)* | **UNCONFIRMED, zero** - see Finding 2 below |
| 43 | *(extension, undocumented)* | **UNCONFIRMED, zero** |
| 44-46 | *(extension, undocumented)* | **UNCONFIRMED, zero** |
| 47 | `AmbientHeightAttenuation` | VERIFIED **for `chara_metal` (a deferred pass)** - reused here in a forward program; not confirmed the same meaning holds (forward programs are fully separate shaders, see `CLAUDE.md`) |
| 49 | *(extension, undocumented)* | **UNCONFIRMED, zero** |
| 50 | `ShadowDepthBias` | VERIFIED **only for `preshading_*`** (unimplemented passes) - same "reused in a different program" caveat as slot 47 |
| 52 | `ShadowMapDimensions` | same caveat |
| 56, 57 | *(extension, undocumented)* | **UNCONFIRMED, zero** - slot 57 is also a `PowExponentSlots` entry (components .2/.3 forced to 1.0 to avoid NaN, but .0/.1 are NOT covered and stay zero) |
| 68 | *(extension, undocumented)* | **UNCONFIRMED, zero** - see Finding 2 below |
| 69 | *(extension, undocumented)* | **UNCONFIRMED, zero** |
| 70 | `Unknown70` | VERIFIED for `chara_metal`; `.z`/`.w` forced non-NaN here, `.x`/`.y` not covered |
| 81 | `VolumeMaskTint` | VERIFIED, populated (inert default) |
| 82 | *(extension, undocumented)* | **UNCONFIRMED, zero** |

**Finding 2 (from the earlier Gemini pass, still unverified by me)**: slots 30 and 68 (both
completely undocumented extension slots, hard zero) get blended together with the confirmed
`WorldFogMaskColor`/`FogFxColor` fields' raw `.rgb` in the same accumulation, feeding directly into
final pixel colour:

```glsl
fma(w0, fp_c9.data[30].x, fma(w1, fp_c9.data[68].x, fma(w2, fp_c9.data[16].x, w3 * fp_c9.data[19].x)))
```

Marrow's fog colours are a deliberately blue-gray placeholder (`0.55, 0.62, 0.72`) chosen assuming
`.a = 0` disables their contribution everywhere - **that assumption does not hold here**, this
formula never reads `.a` at all. Whether `w0..w3` (temp values computed earlier in the shader)
already zero out this whole term in practice is NOT yet confirmed - that's the actual open
question, not the UBO values themselves. **Priority #2 for Ghidra/static trace**: work out from the
full shader text what `w0..w3` (`temp_297`/`temp_307`/`temp_311`/`temp_309` in the extracted
MiasmaTentacle file) actually evaluate to for a normal, non-fogged scene - if they're not already
near-zero, either the fog-colour placeholder needs to become `(0,0,0,0)` or slots 30/68 need real
values.

**Slots to resolve via the un-walked `startDeclare` callers** (per `docs/gsys_environment.md`
section 5 - same list as before, still not walked): `FUN_7100c97b18`, `agl::pfx::Sky::Sky::initialize`,
`agl::fx::Cloud::initialize`, `game::gfx::EnvironmentRenderer::initialize`,
`game::gfx::MiasmaRenderer::initialize`, `game::gfx::DeferredRenderer::initialize`,
`FUN_7100e21940`, `FUN_7100e29aa8`, `FUN_7100e2c7a0`. (Gemini's pass added two new candidate names
- `game::gfx::EnvironmentRenderer::initialize` at `0x7100c7e6ec` and
`game::gfx::MiasmaRenderer::initialize` at `0x7100c88bc0` - worth trying first given the "miasma"
name is directly relevant to the two broken creatures.)

## UBO 4: `_SceneMat` / `fp_c13` (binding 10) - `src/WildRenderingSharp/Shaders/Profiles/Totk/Ubos/SceneMatUbo.cs`

Unlike Env, every field name here is already known for certain (real BFSHA reflection, not
guesswork - see `SceneMatUbo.Fields`). The only open question is which of the 88 fields
`BuildFromLighting` should actually be writing. Slots either forward shader reads, and their
current status:

| Slot (data[N], byte = N*16) | `SceneMatUbo.Fields` name(s) in that slot | Populated? |
|---|---|---|
| 0 | `DynamicToonLightAdjustForDemo` | populated (MID scale) |
| 1, 2, 3 | *(unnamed float packing - shadow/proj-shadow offsets)* | zero |
| 4-9 | `ConstVanishing*` (LocalPosScale, PatternRepeeatRatio, PatternScrollSpeed, Color0Start/End, Color1Start/End, ShavingFetchScale/Offset, WholeAlpha, BecomeMaxIntensityRatio, PatternFetchScale) | zero |
| 13 | `DynamicMonochromeSaturationForChara`/`ForNonChara` | **FIXED this session** -> 1.0 |
| 23 | `ConstFigureParam` | zero |
| 25-28 | `ConstBlueprintBaseAlbedo0/1`, `ConstEdgeClampQuatToHalf`, `ConstBlueprintEdgeColor`, `ConstSkyIslandShadowDensity/OffsetScale` | zero |
| 29 | `ConstBlueprintEmissionColor` (.xyz) / `ConstMiasmaSpecularScale` (.w) | zero - **note**: `ConstMiasmaSpecularScale` sits in the same slot as `ConstBlueprintEmissionColor`, both zero; given the name, worth checking whether a real corrupted-enemy scene should have this nonzero |
| 30 | `ConstBlueprintAlbedoExposureCoef`/`EmissionCoef`/`EmissionExposureCoef`/`ConstAoSatSsao` (4 scalars packed) | zero |
| 33 | `ConstDepthShadowCharaPcf`/`BlurPcf` | zero (`ConstDepthShadowCharaBlurScale` at slot 33+ is set to 1.0, but 33 itself / `CharaPcf` is not) |
| 36 | `SceneShadingInfoExposureBase[0..3]` (unnamed reflection tail) | only `.y` (index 1 of the tail) is explicitly set to `0f`; `.x`/`.z`/`.w` are hard zero |
| 57 | `SceneShadingInfoExposureBase[+21*4]` region | zero |

None of `DarkenvFieldColor`/`DarkenvCharaColor`/`DarkenvCharaSat` (bytes 272/288/304, plausible
"Depths/gloom darkness" names) were observed being read by either of these two specific forward
programs - they may matter for a *different* shading model (e.g. `Enemy_Dragon_Darkness`, an actual
Depths creature) even if not for these two. Worth checking if this investigation expands to a third
model.

## Texture bindings (both forward programs declare the same 17 samplers)

| Unit | Sampler name | Marrow's current binding | Real or placeholder |
|---|---|---|---|
| 6 | `cTex_DepthShadowCascade` (shadow array) | flat "always lit" array | placeholder |
| 7 | `cTex_WorldShadowHeight` | flat white | placeholder |
| 9 | `cTex_Proc3DNoise` | real 64x64x64 BC4 volume | **real** (fixed this session, didn't resolve the bug alone) |
| 10 | `cTex_VolumeMask` | flat `(0,0,0,0)` | placeholder |
| 11 | `cTex_SkyInscatter` | flat white | placeholder |
| 13 | `cTex_Projection0` | flat white | placeholder |
| 14 | `cTex_SkyIslandShadow` | flat white | placeholder |
| 15 | `cTex_MinusFieldDarkness` | flat white | placeholder |
| 17-23 | `cTexture0`-`cTexture6` | the material's own real textures (per-shape sampler binding, unrelated to this pass's own placeholders) | **real** |
| 29 | `cTex_ForwardTerrainAlbedo` (array) | flat white array | placeholder |
| 30 | (`cTex_ColorBuffer`, bound at unit 30 in code) | real copy of the scene colour buffer | **real** |
| 31 | `cTex_MinusFieldLightMap` | flat white | placeholder |
| 5 | (`LinearDepthHalf`, bound at unit 5) | real | **real** |

Nine of seventeen samplers are still flat placeholders. Any of them being sampled and blended in
unconditionally (not gated to zero contribution by some other factor for a normal outdoor daytime
scene) is a candidate contributor to a uniform colour cast - flat white in particular, multiplied
through several of the same accumulation chains Finding 2 already flagged, could easily be part of
"why is everything washed toward one flat colour."

## The missing outline/"sketch lines" mystery - separate from the UBO work above

**Newly reported**: with `ForceForwardOnOpaqueShapes` on, otherwise-correct models lose their
toon/ink outline effect entirely, not just the two broken creatures. **Correction (user)**: the
sketch-line effect is NOT a separate Marrow pass - it's part of TotK's own real decompiled shaders
(almost certainly baked into the deferred G-buffer/resolve programs themselves, the same way every
other bit of shading here comes from the game's real compiled shaders, not a hand-rolled Marrow
effect). Confirmed there's no such thing in `src/WildRenderingSharp/Pipeline` to begin with (grepped for
edge/outline/ink/sketch - nothing relevant). So there's no "reorder pass X vs pass Y" fix available
here at all - the outline is computed once, correctly, by the deferred path, and the forward pass's
full-opaque redraw simply **overwrites those already-correct pixels** with its own outline-less
output on top, because it draws into the same colour buffer as a complete replacement, not a
composited overlay.

This points squarely at the **per-object dispatch question** (the still-unresolved "secondary
thread" from `docs/forward_cyan_bug_research_request.md` - where `gsys::ModelShader::initialize`'s
`+0xC0` assign-bitmask actually gets read at draw time): the real engine most likely does **not**
run `gsys_assign_material` unconditionally for every opaque shape that happens to have a compiled
forward program the way Marrow's opt-in toggle does. Some real condition must gate it (an active
status effect flag on the object, most likely, given the forward program's own branches are keyed
on exactly those flags) - and whatever that condition is, it must normally be FALSE for ordinary
non-corrupted enemies, which is exactly why they'd never lose their outline in the real game. This
also reframes the cyan bug itself: MiasmaTentacle/the Ganondorf-like model presumably DO have that
condition true (they're always meant to be under the corruption effect), so for them the forward
pass is correctly invoked - meaning the cyan colour is a real, in-scope bug in what Marrow feeds
that pass (the UBO work above), not evidence the pass shouldn't run for them at all.

## Investigation log: the per-object dispatch question, resolved as far as static analysis can take it

Traced this directly in Ghidra, several independent angles, all converging on the same answer:

1. **`gsys::ModelShader::initialize` (`0x7100763f04`)** confirmed: the `+0xC0` bitmask is set ONCE,
   at material-shader-load time (called from `gsys::G3dResMaterialEx::updateShader`), and only
   records which assign-pass names the SHADING MODEL declares a compiled program for. It's a
   capability bit, not a per-frame draw decision.
2. **`gsys::ModelSceneContext::pushRenderQueue` (`0x7100c31264`)** does a real bitmask AND
   (`*(uint*)(queue+0x14) & *param3`) but this gates which QUEUE a draw call is routed to, not
   whether an object participates in a queue in the first place - not the mechanism either.
3. **Ruled out a shader-side alpha gate**: hypothesized the forward program's own output alpha
   (`output_color[0].w = fp_c11.data[33].w`, a real material field named
   `gsys_xlu_zprepass_alpha`) might be a per-status computed blend factor, so an inactive object
   would blend at 0% and the correct deferred (outlined) pixel would show through unchanged even if
   the pass ran. Checked the actual baked value for Bokoblin's `Mt_Skin`:
   **`gsys_xlu_zprepass_alpha = 1.0`, a flat material constant, not computed per-pixel.** Even with
   real alpha-blending enabled, 1.0 alpha is a full replace - this isn't the gate.
4. **Ruled out a static per-material RenderInfo flag**: hypothesized `gsys_pass` (a material
   RenderInfo string) might mark which materials are meant to go through
   `gsys_assign_material`. Dumped it for both materials via
   `ShaderLibrary.CompileTool --render-info <Model> --all`: **both Bokoblin's and
   MiasmaTentacle's `Mt_Skin` report `gsys_pass = "no_setting"`, identical.** No static signal here
   distinguishes "should run forward" from "shouldn't."
5. **Found a real, per-instance runtime toggle**: `gsys::ModelNW::setMaterialShaderAssignVariation`
   (`0x7102a416d4`) - confirmed it writes a per-material, per-assign-pass byte on a live model
   instance (`gsys::ModelUnit` has the same method as an empty virtual stub, `ModelNW` is the real
   override). This is almost certainly the actual mechanism. But every xref to it is a `[DATA]`
   (vtable) reference, not a call site - finding who calls it means identifying the interface class
   and then finding every place THAT vtable slot gets invoked, which fans out into gameplay/AI code
   (`game::component::MiasmaConversion` and dozens of `Execute*Miasma*`/`Ganondorf*Miasma*`
   behaviour-tree nodes were the nearest-named candidates, none directly confirmed as the caller).

**Conclusion (as far as this can be pushed without a much larger gameplay-AI excavation)**: the
real per-object gate for `gsys_assign_material` is genuinely **dynamic, gameplay/status-driven
state**, not anything encoded in the static material/shader data Marrow's offline pipeline already
reads. Nothing in the BFRES/BFSHA data distinguishes "this Bokoblin shouldn't get the pass" from
"this MiasmaTentacle should" - the distinction only exists at runtime, driven by whatever game
logic actually calls `setMaterialShaderAssignVariation`, which Marrow has no equivalent system for
and can't easily replicate (Marrow doesn't simulate status effects, AI state, or gameplay
components at all - it's a renderer, not a gameplay engine).

**Practical implication for Marrow**: chasing the exact C++ call site further has a real cost
(possibly many more turns into unfamiliar gameplay-AI code) for a payoff that wouldn't actually be
usable - even with the exact condition in hand, Marrow has no source data (no live status-effect
state) to evaluate it against. The pragmatic fix is architectural, not an RE deliverable:
`ForceForwardOnOpaqueShapes` should stay an explicit, manual, per-shape opt-in (it already is, via
`LightingContext.ForceForwardOnOpaqueShapes` + the Material Inspector's per-shape `Enabled`
right-click toggle) rather than something Marrow tries to auto-decide - the user selectively
enables it on the specific shapes that are ALWAYS meant to look corrupted (MiasmaTentacle,
Ganondorf-like models - these don't need a dynamic condition since they're permanently "affected"
by design), and leaves it off everywhere else. That's not a workaround so much as an accurate
reflection of what's actually knowable here.

## Finding 2 (the fog-colour leak) - DISPROVEN by hand-tracing the full formula

Gemini's report flagged `fp_c9.data[30]`/`data[68]` mixing with the confirmed fog colours
(`data[16]`/`data[19]`) as a likely leak. Rather than trust the pattern match, traced every input
(`temp_297`, `temp_298`, `temp_301`/`temp_302`, `temp_305`/`temp_307`, `temp_309`/`temp_311`) back
to its real definition in `material_prog3248_extracted.frag` (lines ~957-987) by hand, using
Marrow's ACTUAL current UBO values (not guesses):

- `temp_297` multiplies through `Env[29].w` and `Env[30].w`/`Env[28]` - all zero (undocumented
  extension) - **evaluates to exactly 0** (the two `PowExponentSlots` fixes for `(29,0)`/`(29,2)`
  already prevent the NaN case, so this cleanly collapses to 0 rather than blowing up).
- `temp_298` ends in an explicit `* fp_c9.data[68].w` - Env[68] is zero - **evaluates to exactly
  0**, independent of everything else in that expression.
- With `temp_297 = 0`, `temp_305 = temp_301` and `temp_307 = temp_298 * temp_301 = 0`.
- `temp_296` (feeding `temp_302`) multiplies through `Env[27].z`, also zero (undocumented
  extension) - **evaluates to exactly 0**, making `temp_302 = 0` too.
- `temp_292`/`temp_293` (feeding `temp_311`/`temp_309`) each end in `* fp_c9.data[16].w` /
  `* fp_c9.data[19].w` respectively - `WorldFogMaskColor`/`FogFxColor`'s alpha, which Marrow
  DELIBERATELY sets to 0 (fog disabled) - **both evaluate to exactly 0**.

Every term in the accumulation `fma(temp_297, Env[30].x, fma(temp_295*temp_260.x, temp_302,
fma(temp_307, Env[68].x, fma(temp_311, Env[16].x, temp_309*Env[19].x))))` is multiplied by
something that's currently exactly zero. **The whole expression evaluates to 0 given Marrow's
current data - the fog-colour leak is not real.** Env[30]/Env[68]'s own values don't even matter
right now (they're multiplied by things that are already zero) - extending their recovery would
not change anything about the current cyan bug. This finding is retracted; the placeholder fog
colour was a red herring.

**Bonus finding from the same trace**: `cTex_Projection0` (unit 13) and `cTex_SkyIslandShadow`
(unit 14) are both in `ForwardPass.WhiteNeutralUnits` (flat white placeholders), which makes
Context slot 35/36's exact meaning (the earlier "is it really frustum planes?" question)
**currently irrelevant** - whatever UV gets computed from it samples a constant-white texture
either way, so that thread isn't worth pursuing further until real projection/shadow textures
exist. More importantly: the shadow-cascade contrast-remap term
(`clamp(fma(cascadeSample*0.25, SceneMat[3].z, -0.35) * 8, 0, 1)`) evaluates to a **hard 0**
given Marrow's neutral "always lit" cascade placeholder (comparison result 1.0) combined with
`SceneMat[3].z` also being zero (unpopulated) - this is NOT the intended "no shadow" neutral value
for this specific formula (it's a contrast-boost curve, not a pass-through), so `temp_319` (which
directly gates part of the main lighting term via `Env[5]`/`SceneMat[0].x`) collapses from what
should be a real, populated value down to 0. This is a genuine, newly-found, real bug - worth
pursuing further, though it wasn't confirmed to be a full explanation for the cyan colour by
itself and the trace wasn't carried further than this in the time available this session.

## Remaining, still-worthwhile UBO work (for the "always corrupted" creatures specifically)

For models that ARE always meant to run this pass (MiasmaTentacle, Ganondorf-likes), the cyan
colour is still a real, in-scope Marrow bug in what gets fed to a pass that's legitimately running -
this part is still worth finishing. Superseded by the hand-trace above: Context slot 35/36 and the
Env[30]/Env[68] extension recovery are both DEPROIRITIZED (proven not to matter for the current
result - see Finding 2's disproof). Next real leads, in order:

1. **Traced `temp_319`'s downstream effect**: when NOT collapsed to 0, the term it gates -
   `fma(temp_319, fma(temp_269,-0.8,Env[5].x), temp_269*0.8) * SceneMat[0].x` - algebraically
   simplifies at `temp_319 = 1` to exactly `Env[5].x * SceneMat[0].x` = **the real sun colour
   (`LightColor0`, already correctly populated) times the mid-tone scale**. At `temp_319 = 0`
   (Marrow's current, collapsed value) it instead falls back to `temp_269 * 0.8` - a flat multiple
   of a local geometric term, with NO sun-colour tint at all. This is a coherent, plausible
   mechanism for a cool/cyan-shifted result: the warm sun tint that would normally counterbalance
   cooler tones elsewhere in the shader never gets applied.
2. **Found the real field via Ghidra** (same technique as the saturation fix - found the exact
   uniform-name string and its write site): `p_dynamic_depth_shadow_scale` (`SceneMat[3].z`,
   `Fields.DynamicDepthShadowScale`) is written by
   `game::gfx::ModelSceneExtension::setDynamicShadowParams` (`0x7100b4f124`) as `1.0 - param_2`,
   alongside two siblings in the SAME function - `p_dynamic_depth_shadow_off` (`= 1.0 - param_1`,
   `Fields.DynamicDepthShadowOff`) and `p_dynamic_world_shadow_off`
   (`Fields.DynamicWorldShadowOff`) - all currently left at Marrow's zero default too. Genuinely
   dynamic (weather/time-of-day driven, hence the name), not a fixed engine constant - there's no
   single "the real default" to just copy in.
3. **NOT YET FIXED - the exact value is still uncertain.** Hand-checking the arithmetic
   (`clamp(fma(cascadeSample*0.25, SceneMat[3].z, -0.35) * 8, 0, 1)` needs `sample*Z >= 1.9` to
   reach 1) suggests even a plausible-looking default like `Z = 1.0` (matching `param_2 = 0` for
   "clear weather") would NOT make Marrow's flat "always lit" cascade placeholder (`sample = 1.0`)
   reach a clean result either - meaning either this formula's real intended operating range is
   different from what a flat placeholder can represent at all, or there's a arithmetic/transcription
   slip somewhere in this trace that needs rechecking before writing any value into `SceneMatUbo`.
   **Do not guess a value in without re-verifying this** - a wrong constant here is exactly the kind
   of confident-but-unverified mistake this whole session has been trying to avoid. Also note
   `Enemy_Bokoblin` (an "unaffected" creature) hits this exact same collapse - it's a general gap,
   not specific to corruption.
4. Ruled out as not mattering right now (don't spend more time here unless the above changes the
   picture): Context slot 35/36's real meaning - it feeds a UV into `cTex_Projection0`, which is
   currently a flat white placeholder either way, so the UV computed from it can't currently affect
   anything. Revisit only once/if a real Projection0 texture is wired up.
