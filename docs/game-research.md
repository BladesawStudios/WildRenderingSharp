# What is known about TotK's renderer

Conclusions only, from the game binary (Ghidra), GPU captures (RenderDoc) and the romfs. Uniform block details are in [uniform_blocks.md](uniform_blocks.md).

## Sky

- The sky is Bruneton precomputed scattering, not an analytic gradient. The twelve `sky_*` programs from `agl_technique_pfx.sharcb` bake a 2D inscatter table once; each frame `sky_postfx_sky` does one lookup into it. `SkyPrecomputePass` runs the chain and `SkyPostFxPass` draws the sky.
- The table is indexed by `v = dir.y * 0.5 + 0.5` and a non-linear sun-view angle. Its alpha plus a bias blends toward a base colour; one scalar scales the colour. `BAKED_SUNVIEW_NON_LINEAR` must be set the same in the baking and sampling programs.
- The scattering coefficients and scale heights come from `master_field.baglsky` (`SkyPostFx`). The palette's `SkyRParam_*` amplifiers and Mie asymmetry (0.75 in the capture) tune them per scene.
- Verified against the game's own table: `SizeInfo` is bit-exact, `betaR` is exact, and the table correlates at 0.95 with six scattering orders.
- `sky_postfx_ground` is aerial perspective over geometry, not part of drawing the sky.
- Sampler names in these programs are `fp_t_tcb_<hex>` with slot = 8 + 2 × the archive's location, not list order. The real vertex quad is ±0.5, not NDC.

## Environment palettes

- About 130 palettes in `Pack/EnvPalette.pack.zs` (BYML, one per place, weather and time of day) author the sky's look: fog colours and a second height fog, bloom, sun colour, two cloud shading sets, the Rayleigh and Mie amplifiers, and exposure.
- `EnvPaletteTimeTable.pack.zs` holds the schedule that picks a palette by time of day.
- The sun moves with the clock, so a palette is never lit from a different sun (`PaletteSun`).

## Clouds

- Clouds are real dome geometry drawn by the game's own `agl_cloud` program, not a raymarch. The dome masks are BNTX textures inside the `collect.genvres` of the genvb (`TotkCloudMasks`).
- Each layer's shading is the palette's four colours (base, highlight, shadow, backlight) with intensities, plus `CloudParamN` blocks from `master_field.baglclwd`.

## `gsys_environment` (Env, binding 6)

- 1328 bytes. Slots 0 to 25 are read from the game's own declarations (`gsys::ModelRenderContext::initialize`, 35 records) and are named.
- Slots 26 to 82 are inferred from how 286 shaders use them. Of those, 43 are read. They are mostly scale and bias fade pairs, effect strengths and additive colours, all neutral at zero.
- Two cases are not neutral at zero. A pow exponent of 0 becomes NaN through `exp2(log2(x) * 0)`, so those slots are forced to 1. A direction of zero is degenerate; `Env[23]` (`cLightDir0World`) is the one with readers and is written.
- High confidence: `[45]` and `[46]` are the Depths darkness map's UV transform and blend, `[68].w` is the cloud-shadow intensity, `[69]` is its noise remap, `[32]` and `[33]` are the wind sample basis, and `[51]` and `[57].w` are the rim light colour and power.
- The remaining extension slots stay unnamed until the other `startDeclare` callers are walked (`agl::pfx::Sky::Sky::initialize`, `agl::fx::Cloud::initialize` and others).

## Forward pass

- The forward `gsys_assign_material` programs read four blocks: `_Context` (1), `_Mat` (8), `_Env` (6) and `_SceneMat` (10). `_Mat` is built per material offline.
- Nine of its 17 samplers are neutral placeholders here. The unit 6 shadow cascade placeholder (always lit) collapses the shadow contrast term to 0, so `DynamicDepthShadowScale` (`SceneMat[3].z`, set by `ModelSceneExtension::setDynamicShadowParams` as `1 - param`) is left unwritten.
- The fog colours in `Env[16]` and `Env[19]` do not leak into the forward result: every term that multiplies them is zero by construction.
- The toon outline lives in the deferred programs. A forward redraw of an opaque shape overwrites it, so forcing the forward pass is a per-shape choice, not an automatic one.

## Pre-shading passes

- A G-buffer program writes a material ID into attachment 0's red byte. `g_buffer_expand_material_id` turns it into depth (`id * (1 - k) + k`, `k = Context[147].z`), and each deferred pass draws a full-screen quad at its priority's depth and depth-tests against it.
- The priority is a float at `Mat[25].x`, nibble-swapped by the vertex shader. Resolve priorities: field_hybrid 0, leaf 2, entrance 6, chara_nonmetal 8, metal 9, grossy 10, hair 11, skin 12, eye 13, miasma 32, water 35, clear 128. Implemented in `GBufferMaterialIds`, `DeferredPassPriorities` and `MaterialIdPass`.
- The `preshading_*` programs that produce `cTex_PreShadow`, `cTex_PreMisc` and `cTex_PreFog` are extracted but not run; those inputs are synthesised. Output location 4 is very likely `PreShadow` and location 2 `PreFog`. Their cascade parameters come from Env 44 to 57 and SceneMat 36 to 39, which are not filled.
