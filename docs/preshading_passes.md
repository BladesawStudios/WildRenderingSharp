# The `preshading_*` passes (not yet run)

WRS lights with the game's `field_*`/`chara_*` resolve programs but still **synthesises** their
`cTex_PreShadow`, `cTex_PreMisc` and `cTex_PreFog` inputs (`ScreenSpaceShadowAndAoPass`, a neutral
PreFog). The game makes them with the `preshading_*` programs. Running those is the missing step; this
is what is known and what blocks it.

## Which pass lights a pixel (done)

- A G-buffer program writes a material ID into attachment 0's red byte (location 0, `cTex_GBuffMaterialID`).
  The IDs are constants in the program (character shaders pick between two with a uniform).
- `gsys::GBuffer::expandMaterialID_` runs the `g_buffer_expand_material_id` program
  (`Lib/gsys/gsys_resource.Nin_NX_NVN.release.sarc.zs` -> `common.sharcb`) to turn that byte into depth:
  `gl_FragDepth = id*(1-k)+k`, `k = Context[147].z`.
- Every `SystemModel.DeferredMain` pass draws a full-screen quad at the depth of its priority and tests
  `gsys_depth_test_func` against it. The priority is a float at `Mat[25].x` (byte 400 of the pass's
  `gsys_material.bin`) - not the `gsys_priority` render info. The vertex shader nibble-swaps it
  (`(p&15)<<4 | p>>4`), as the expand step's SWAP variant does to the ID, which is why `preshading_chara`
  (priority 8, `lequal`) covers every ID whose low nibble is >= 8, i.e. the character classes.
- Priorities: field_hybrid 0, field_leaf 2, field_entrance 6, chara_nonmetal 8, metal 9, grossy 10, hair 11,
  skin 12, eye 13, field_miasma 32, field_water 35, clear 128. Preshading: field 0, field_shadow 0, leaf 2,
  entrance 6, chara 8 (lequal), field_xlu 16, chara_xlu 24, water 48, clear 128.
- Implemented for the main passes: `GBufferMaterialIds`, `DeferredPassPriorities`, `MaterialIdPass`.
- Unwritten pixels hold ID 0 (what the field pass lights). `preshading_clear`/`clear` (128) look like the
  sky: pixels with no geometry.

## What the preshading programs are

Programs are already extracted as `deferred_preshading_*_extracted.{vert,frag}`. Same UBOs as `field_hybrid`
(Context 1, Env 6, Mat 8, SceneMat 10). `preshading_field` and `_field_shadow` are tile-instanced like
`field_hybrid` (`vp_s0`, flag at `0x41C0 + 16 i + 12`), and `preshading_field`/`leaf`/`entrance`/`xlu`
**write** an SSBO (`fp_s0`, binding 0) - the per-tile flags that choose `field_hybrid` vs `_all_shadow`.

Samplers (fixed units): 0 Albedo, 1 Normal, 2 MaterialID, 3 Emission, 4 NormalizedLinearDepth, 5 Half,
6 `sampler2DArrayShadow` DepthShadowCascade, 7 DepthBuffer, 8 WorldShadowHeight, 9 CubeEnvMap, 11 VolumeMask,
12 SkyInscatter, 13 Projection0, 14 SkyIslandShadow, 15 MinusFieldDarkness, 16 PreShadow, 18 PreMisc,
25 LocalReflection, 26 DeferredCloudNoise, 27 DeferredSkyColor (2D array), 28 DeferredLightPrePass.

Outputs: locations 0, 1, 2, 4, 5 (`preshading_chara`/`clear`: 0, 1, 4, 5).

## What is still unknown (why it is not done)

1. **Output meaning.** The G-buffer render buffer has entries Albedo (loc 1), Normal (3), MaterialID (0),
   Emission (5) and **Shadow (4)** (`GBuffer(Shadow)`). Loc 4 is very likely `cTex_PreShadow`: `field_hybrid`
   reads `PreShadow.xzw`, and `preshading_field` writes `(x, 0.0, z, w)`. Loc 2 (field family only, `w = 1`)
   is probably `PreFog`. Loc 0 is unclear: `preshading_field` writes `(PreShadow.w, y, PreShadow.z, w)`,
   `preshading_clear` writes the constant `(1, 0, 1, 1)`; it may be `PreMisc` or a rewritten MaterialID.
   Locs 1 and 5 end with `w = 1.0` (the albedo-alpha emission-enable bit); `preshading_clear` writes the sky
   colour to loc 5, so 5 is probably emission/pre-lit radiance and 1 a rewritten albedo.
2. **Inputs.** The shaders read `PreShadow`/`PreMisc` and also sample the shadow cascades themselves. The
   cascade parameters come from Env slots 44-57 (`[56].xyz`, `[51].z`, `[52]` read 28 times) and SceneMat
   36-39, which WRS does not fill. Without them the shadow comes out wrong.
3. Neutral inputs are needed for units 8, 12, 13, 14, 15, 25, 26 and 27; what "no effect" is for each is unknown.
4. The expand step's mask/swap variants and the mode used for preshading (`expandMaterialID_(.., 1, 2)` from
   `ModelSceneBuffer::beginGBufferPreShading`) are chosen from config bytes at `GBuffer+0x13d0` that were
   not traced. The clear colour of the MaterialID entry is also unconfirmed.

## Plan

Gate each preshading draw with a stencil holding `swap(id)` (stencil func from the pass's depth func, ref
`swap(priority)`; uncovered pixels = 128), tile-instance the field ones, give the programs
`early_fragment_tests`, write to separate textures and consume them in
`DeferredResolvePass.BindResolveInputs` in place of `targets.PreShadow/PreMisc/_texPreFog`, behind a setting.
A RenderDoc capture of one frame would settle items 1 and 2 directly (the render targets, and the Env and
SceneMat contents, at the preshading draws).
