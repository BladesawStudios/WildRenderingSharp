# Captured cloud masks — a deliberate, temporary exception

These three files are the **raw BC4_UNORM block data** (512x512, mip 0) that TotK itself binds at
its real cloud draw, lifted from a GPU capture. They are checked in, which is the one place this
project ships game data rather than extracting it from the user's own ROM.

## Why they are here

The clouds' masks are **not romfs assets**. A byte-exact content scan of all ~29,000
`TexToGo/*.txtg` finds no match (best score 37.9%, i.e. noise). They are generated at runtime by
the real `noise_cloud` program, which resolves through a name->texture dispatcher to a
dynamically-baked resource called `cloud_noise`.

Marrow previously used `PolarSphereMappingNoise_Fi` and `VolumeMist03`, picked by searching for a
plausible NAME at the right size and format. Both are wrong - byte-comparison correlation against
what the game actually binds is ~0.01-0.10, i.e. unrelated images.

## Why shipping them is defensible, and where it is not

Unlike a captured *sky* LUT, these are **static**: baked once at init from static parameters, and
identical regardless of time of day or weather. So a captured copy is the correct, complete data
rather than one frame's state - it does not carry the "only works on some palettes" hazard.

It does still break the from-the-ROM rule, and that is the reason this is temporary.

## Replacing them (the actual fix)

Run `noise_cloud` and bake them locally. All three `RENDER_TYPE` variants are already extracted
(`agl_noise_cloud_type0/1/2`). What is missing is the two parameter blocks it reads -
`ProceduralNoiseCommon` (48 B) and `CloudNoiseParam` (64 B), about 13 floats - which are not
authored anywhere findable: there is no procedural-noise file among the 27 postfx entries in
`Env/GameScene.Nin_NX_NVN.genvb.zs`, the bake draw is not in a mid-game capture (it runs once at
init), and the generator is not symbolised in the binary.

These files are the fitting target for that work: bake each variant, diff against them, and when
the fit lands, delete this directory. Note the game binds `cBaseTexture` and `cBaseTexture_Blend`
to the SAME texture, but `cNoiseTexture` and `cNoiseTexture_Blend` to DIFFERENT ones - which is
what suggests two of the three `RENDER_TYPE` variants.
