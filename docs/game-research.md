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
- `gsys::ModelScene::getDrawArg` lists the game's passes with a material-mask word each: `GBuffer/Xlu` (`0x10`), `GBuffer/XluSeal` (`0x100`), `GBuffer/XluToOpa` (`0x20`) and the forward `Model(Xlu)` (`0x40000000`). A `gsys_gbuffer_xlu` material such as Purah's lens has a program for all three of the G-buffer, z-only and forward passes, so it is drawn in the G-buffer scene-colour pass and again forward; the forward fresnel alpha is near zero face-on and only brightens toward the rim.
- Every material authors a `display_face` (52 of 53 sampled say `front`). The forward and scene-colour passes cull by it; the G-buffer pass is sorted and merged and still draws double-sided, which only matters for a shape that is blended or writes no depth.
- `cTex_ColorBuffer` is the frame times `Exposure / EmissionScale` in the scene-colour pass, and the forward pass draws in those units too, converting back when it composites. This matches the two passes to each other; it is not read from the game.
- The toon outline lives in the deferred programs. A forward redraw of an opaque shape overwrites it, so forcing the forward pass is a per-shape choice, not an automatic one.

## G-buffer albedo

- The G-buffer program writes the albedo it sampled (`cTexture0.xyz`, read through an sRGB view, so linear) straight to attachment 1, and the resolve programs read `cTex_GBuffAlbedo` and use it with no gamma of their own. Both only work if attachment 1 is an sRGB target, so the hardware encodes on write and decodes on read; `RenderTargets` makes it `SRGB8_ALPHA8` and turns `FRAMEBUFFER_SRGB` on while the G-buffer is bound. As a plain linear RGBA8 a dark albedo had a few levels per channel, and showed as coloured squares where its texture varies (the Giants' bodies, which are nearly black). The alpha is flag bits and is not encoded.
- The terrain's copy of the albedo under it is the same format.

## Pre-shading passes

- Gloom (`miasma_renderer_shape`, `_count`, `_compose`) is three agl programs in `Shader/ApplicationPackage`'s `AglShader.sharcb`, exported to `agl_miasma_renderer_*` by `TotkAglShaders.Gloom`. Shape has the macros `IS_ENABLE_NOISE`, `IS_ENABLE_HALF`, `BLEND_TYPE` (0 max, 1 the cancelling min) and `SHAPE_TYPE` (0 sphere, 1 box, 2 cylinder, 3 the altitude gloom); compose has `ENABLE_CORRECT`, `ENABLE_TILE_MODE` and `ENABLE_ALPHA_MODE`, and MiasmaSharp's port is the 0/0/0 one. Count has no pixel stage. Shape writes an RG density target and a bit per area into the storage buffer `fp_s0` the game reads back to cull; it reads `fp_c3` (view), `fp_c4`, `fp_c5` (the area) and one 2D depth sampler plus its noise volumes.
- Compose draws three targets, not the two the port writes: location 0 is the emission, 1 the albedo with the flag byte in alpha, and 2 the material ID, `(32/255, 0, fade, 0)`. 32 is the priority of the `field_miasma` pass, so gloom is lit by that pass, which reads the fade from the ID's blue. Its slots: `fp_c3` has the view's third row at `[2].xyz`, the view-to-world rows of x, y and z at `[7]`, `[8]` and `[9]` with the translation in `.w`, the depth to view depth scale and bias at `[11].xy` and the distance scale at `[13].y`; `fp_c4` has the dark dump at `[1]`, the base colours at `[2]` (rgb, the black pattern density) and `[3]`, the black colour at `[4]`, the albedo and emission intensities at `[5].xy`, the red pattern at `[6].xy`, the grain colour at `[7].xyz`, the grain pattern at `[8].xy` and the clock at `[9].xyz`; its vertex stage reads the tangents of half the field of view at `vp_c3[10].xy`. Its samplers, by location (`fp_t_tcb_<hex>` is 8 + 2 * location): 0 the depth, 1 the density, 2 the voronoise cloud, 3 the simple gradient, 4 the fbm gradient, 5 the simple Worley, 6 the environment mask and 7 the material ID.
- The shape programs' blocks move by variant: the adding variants (`BLEND_TYPE0`) read a context block at `fp_c3`, the view at `fp_c4` and the area at `fp_c5`, while the cancelling ones (`BLEND_TYPE1`) have no context block and read the view at `fp_c3` and the area at `fp_c4`; the vertex stage reads the view at `vp_c3` and the area's first three rows (the unit box to the world) at `vp_c4` in both. The area is a sphere's view-space centre and radius over 0.88 at `[3]` with density and fade at `[4].x` and `[4].z`; a box's view-to-unit rows at `[3..5]` with density and fade at `[6].x` and `[6].z`; a cylinder's the same rows with the top and bottom densities, the noise scale and the fade at `[6]` and the inner ratios at `[7].xy`; the altitude gloom's density at `[0].x`, drawn over the screen by the compose program's quad stage. A cube drawn past the camera's far plane is clipped away whole, so a volume has to stand within it.
- The compose program discards where the density's red or green is not above zero, and where the material ID's green has bit 32; the gloom stage writes the density target with fade one for an adding volume and zero for a cancelling one.
- The Depths' ground is `cave_quadmesh.bfsha`, whose models are `cave_quad_mesh_generic` and one for each of lod0, lod1 with the soft edge on and off, lod2 and lodfar. The generic one's programs 1 and 2 are exported as `depths_prog1` and `depths_prog2`: the G-buffer program writes locations 1, 3, 5 (the emission) and 7, reads `_cave_StaticDataUBO` (binding 11), `Context` and `Mat`, and samples `cCaveQuadMeshNormals` (17), `cCaveQuadMeshMaterialWeights_Ao` (18), `cCaveMaterialWeightsDetail` (19) and the two terrain material arrays.
- The Depths' vertex stage branches through a jump table held in the program's immediate constants, so it decompiles only when the translator is given them (`TegraShaderTranslator.Decompile(code, constants)`); without them it comes out as an empty loop, which is what the first export was. It is not a vertex-buffer draw: it reads only `gl_VertexID`, which packs a leaf's column in bits 0-4, its row in bits 5-9 and the quad node's index in bits 10 and up, and takes everything else from three uniform blocks and a storage buffer: the draw call (binding 3, the level of detail in the high half of `[1].z` and a bit mask at `[1].w`), the instance (12, a placement matrix in `[0..2]`, the depth of the node tree at `[3].y`) and the page layout (13, the byte offsets into the storage buffer at binding 0 of the node headers and the heights, picked by the node's size class 240, 256 or 280). It writes six interpolants, which the fragment stage reads as: three components of 0; x, y and w of 1, of which it divides the first two by the last; x, y and z of 2, the first and last scaled by 0.0133 for the detail texels; the y of 3, flat; and the x and y of 4 and of 5, of which 4 is the uv it fetches the normal and weight textures at. Drawing it means building the game's page pool from the quad resource - the node headers, the heights, the two textures and the three blocks - which is not done.
- A G-buffer program writes a material ID into attachment 0's red byte. `g_buffer_expand_material_id` turns it into depth (`id * (1 - k) + k`, `k = Context[147].z`), and each deferred pass draws a full-screen quad at its priority's depth and depth-tests against it.
- The priority is a float at `Mat[25].x`, nibble-swapped by the vertex shader. Resolve priorities: field_hybrid 0, leaf 2, entrance 6, chara_nonmetal 8, metal 9, grossy 10, hair 11, skin 12, eye 13, miasma 32, water 35, clear 128. Implemented in `GBufferMaterialIds`, `DeferredPassPriorities` and `MaterialIdPass`.
- The `preshading_*` programs that produce `cTex_PreShadow`, `cTex_PreMisc` and `cTex_PreFog` are extracted but not run; those inputs are synthesised. Output location 4 is very likely `PreShadow` and location 2 `PreFog`. Their cascade parameters come from Env 44 to 57 and SceneMat 36 to 39, which are not filled.
