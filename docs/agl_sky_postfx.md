# `agl::pfx::Sky` — the real sky shaders, extracted and decoded

Source: `Lib/agl/agl_resource.Nin_NX_NVN.release.sarc.zs` → **`agl_technique_pfx.sharcb`** (54
programs). Extracted by `TestAglShader.ExtractSkyPostFxShaders`, wired into startup via
`ModelPreparer.EnsureSkyShaders`, CLI `--extract-sky-shaders`. Discover with
`--list-agl-programs <romfs> sky --combos`.

**Nothing in the live pipeline links these yet.** `BackgroundPass` still runs its own hand-written
Rayleigh+Mie raymarch. This document is what a wiring effort starts from.

## The shape of the real sky: two phases

TotK's sky is **Bruneton precomputed atmospheric scattering**, not an analytic gradient. All the
physics runs once, offline, into a LUT; the per-frame pass is a single texture lookup.

| Phase | Programs | Cost |
|---|---|---|
| Precompute | `sky_transmittance` → `sky_irradiance` → `sky_inscatter` → `sky_delta_inscatter` → `sky_copy_inscatter`/`sky_copy_irradiance` → `sky_bake_inscatter`/`sky_bake_irradiance`/`sky_bake_range_transmittance` | once |
| Per frame | `sky_postfx_sky`, `sky_postfx_ground` | one quad |

`LOCAL_STEP` on the precompute programs is **not a variant to choose between** — it names which
iteration of the multiple-scattering solve the program is, so every declared value is a separate
required pass. All are extracted (`agl_<name>_step<N>.{vert,frag}`).

## `sky_postfx_sky` decoded — the whole per-frame sky

The extracted fragment shader is ~120 lines and does exactly this:

```glsl
vec3 d = normalize(in_attr0.xyz);          // view ray
float v = d.y * 0.5 + 0.5;                 // LUT V: ray elevation
float t = dot(d, fp_c4.data[2].xyz) * 0.5 + 0.5;   // sun-view angle, remapped
float u = 1.0 - acos(t) * (2.0 / PI);      // LUT U: non-linear in sun-view angle
vec4  lut = texture(cTexBakedInscatter, vec2(u, v));
float w   = clamp(lut.a + fp_c4.data[6].w, 0.0, 1.0);
out.rgb   = mix(fp_c4.data[6].xyz, lut.rgb * fp_c3.data[13].x, w);
out.a     = 1.0;
```

The `acos` is not literally in the ISA — it is the standard polynomial approximation, and finding
its exact coefficients baked in (`1.5707288, -0.2121144, 0.0742610, -0.0187293`, then `× 2/π`)
is what **confirms** the extracted variant really is `BAKED_SUNVIEW_NON_LINEAR=1`, and tells you
precisely what that macro means: LUT precision is spent near the horizon, where the gradient is
steepest.

### Resolved uniform mapping

NVN indices map as `fp_c<location+3>`, which the archive's own block sizes corroborate — every
slot referenced lands inside its block:

| Block | Size | Slot | Meaning |
|---|---|---|---|
| `RenderInfo` (`fp_c4`) | 112 B = 7 vec4 | `[2].xyz` | sun direction (world) |
| | | `[6].xyz` | base/fog colour the LUT is blended toward |
| | | `[6].w` | bias added to the LUT's alpha before clamping |
| `Context` (`fp_c3`) | 224 B = 14 vec4 | `[13].x` | **scalar intensity applied to the LUT colour** |

`fp_c3.data[13].x` is the sky's single brightness knob, and `data[13]` is the last vec4 of
`Context` — consistent, not a stray read.

## Macro choices, and why each is forced

Read off the per-combination sampler/block matrix, not guessed:

| Macro | Chosen | Why |
|---|---|---|
| `USE_TONEMAP` | 0 | =1 adds no sampler and no block, only pixel bytecode — an inline tonemap. Marrow already ends its frame with the real `agl_hdr_compose`; =1 tonemaps twice. Verified: the extracted `.frag` contains **zero** `pow`/`log2`/`exp2`. |
| `RENDER_CLOUD` | 0 | =1 adds `cTexCloud`, a screen-space cloud buffer Marrow doesn't produce (its clouds are a separate geometric dome). |
| `USE_ADHOC_FOG` | 0 | Adds no inputs either way ⇒ an extra authored fog term, not a required one. |
| `USE_FOG_DENSITY` | 0 | ground only; =1 adds `cTexFogDensity`, no source in Marrow. |
| `USE_LINEAR_DEPTH` | 1 | ground only; this variant needs **fewer** inputs — drops `RenderInfo@2` from the vertex stage. |
| `BAKED_SUNVIEW_NON_LINEAR` | 1 | **A pairing constraint, not a free choice** — both the baking program and the sampling programs carry it and must agree, or the lookup is simply wrong. Flip it in all of them together or not at all. |
| `RENDER_SUN` | both | =1 additionally samples `cTexTransmittance` (producible — it's the chain's own first step) *and relocates every block* (Context 0→1, RenderInfo 1→2, new SizeInfo@0). Kept as a separate file (`agl_sky_postfx_sky_sun`) so that layout difference can't be silently wrong. |

Extracted output matches the archive metadata exactly: plain sky 1 sampler / 2 blocks, sun variant
2 samplers / 3 blocks, ground 3 samplers (`cTexBakedInscatter`, `cTexColor`, `cTexDepth`) / 2 blocks.

## Running the chain: `SkyPrecomputePass`

`src/WildRenderingSharp/Profiles/Totk/Sky/SkyPrecomputePass.cs` runs the real chain. **Transmittance is implemented and
numerically verified; the inscatter stages are not yet** (see the blocker below).

### The two blocks are opaque blobs — no name table exists

`agl::pfx::Sky::Sky::initialize` (`0x7100c94670`) declares both of its blocks as **type `0x13`
(blob)**: `declare_(…, 0x13, 1, 0x90, 0x10)` = one 144-byte blob (`SizeInfo`) and `… 0x80 …` = one
128-byte blob (`RenderInfo`). So unlike `gsys_env`'s base region, there is nothing to walk — these
are struct memcpys, and the layouts below are necessarily inferred from use.

`Config` (48 B) is declared elsewhere; NVN index = block location + 3, so `fp_c3` = `SizeInfo`,
and `fp_c4` = `Config` on the solve passes but `RenderInfo` on the bake/copy passes (which is why
`bake_inscatter` reaches `data[7]` — 128 B is 8 vec4s, 48 B only 3).

### Decoded

| Block | Slot | Meaning | Evidence |
|---|---|---|---|
| `SizeInfo` | any texture slot | `(w, h, 1/w, 1/h)` | `transmittance`, `copy_irradiance` and `bake_inscatter` all compute `fragCoord/render_scale * data[N].zw` |
| `SizeInfo` | `[0]`, `[1]`, `[2]` | transmittance / irradiance / baked-inscatter dims | which pass reads which |
| `SizeInfo` | `[5].x`, `[6].x` | the two axes whose product is the packed inscatter width | `copy_inscatter.vert` computes `data[5].x * data[6].x` |
| `SizeInfo` | `[8].x`, `[8].y` | **`Rg`**, **`Rt`** (ground / atmosphere-top radii) | used throughout as `x*x`, `y*y`, `mix(x, y, t)` |
| `Config` | `[0].xyzw` | **`betaR.rgb`, `betaM`** | the four multipliers on the finished optical depths at the end of `inscatter_step1` |
| `Config` | `[1].y`, `[1].z` | **`HR`**, **`HM`** scale heights | read only as `1.0/data[1].y`, `1.0/data[1].z` |
| `RenderInfo` | `[3].w` | **layer index** for the 3D LUTs | `copy_inscatter.vert`: `data[7].x * layer - layer` |

`Config` is *real authored ROM data Marrow already parses* — `SkyPostFx.RayleighScatteringCoeff`,
`MieScatteringCoeff`, `RayleighBaseHeight`, `MieBaseHeight`, out of `master_field.baglsky`. Nothing
is hardcoded, which is what makes this portable.

### Two details that would silently break it

- The vertex stage does `gl_Position.xy = in_attr0.xy * 2.0` — the real vertex buffer is a
  **half-unit quad (±0.5)**, not NDC. Feeding NDC draws at double size.
- Sampler names are `fp_t_tcb_<hex>` where **slot = 8 + 2 × the archive's `Location`**, and that is
  *not* list order. Verified against five independently extracted programs. Binding by list
  position silently swaps textures rather than failing.
- These shaders divide by `support_buffer.render_scale[0]`, so `SupportBufferUbo` must be bound.

### Transmittance: verified

The LUT dimensions are Bruneton's canonical ones, which is a free choice, not a recovered
constant — the shaders read their dimensions *out of* `SizeInfo`, so only internal consistency
matters.

`SkyPrecomputePass.Verify()` reads the LUT back and checks properties any correct transmittance
table must have regardless of parameterisation — this is a precompute LUT with no on-screen
appearance, so "no GL error" proves nothing, and the project's own agreement is to prove a step
with data rather than a picture. Result at 256×64:

```
range [0.000000, 0.999512], non-finite=0, out-of-range=0, varies=True
monotonicity: X falling=6445 rising=0 (rising), Y falling=7048 rising=0 (rising) -> PASS
```

**Zero monotonicity violations on both axes** out of 16384 samples, spanning the full [0,1] range.
Both axes increase toward less optical depth, consistent with Bruneton's `(r, mu)` parameterisation.
This validates `Config`, the `SizeInfo` convention, `Rg`/`Rt`, the quad, and the support buffer in
one shot: each of those being wrong has a distinctive failure signature (all-zero, all-one, NaN, or
non-monotonic noise) rather than a clean monotonic table.

### `SizeInfo` — fully recovered from a capture

Read out of the game's own bound constant buffer with the RenderDoc MCP
(`C:\Users\dylan\repos\RenderDocMCP`). It could **not** be found by size — the emulator declares
every block as `data[4096]` and rounds UBO bindings up to 256-byte alignment, so the 144-byte
struct is indistinguishable from a 224-byte one. It was found by content instead, searching every
draw's constant buffers for a vec4 shaped `(a, b, 1/a, 1/b)`:

| Slot | Captured value | Meaning |
|---|---|---|
| `[0]` | `256, 64, 1/256, 1/64` | transmittance dims |
| `[1]` | `64, 64, 1/64, 1/64` | irradiance |
| `[2]` | `256, 256, 1/256, 1/256` | baked inscatter |
| `[3]` | `64, 64, 1/64, 1/64` | range transmittance |
| `[4]` | `32, 1/32, 1/31, 1/15` | `RES_MU` |
| `[5]` | `32, 1/32, 1/31, 1/15` | `RES_MU_S` |
| `[6]` | `8, 1/8, 1/7, 1/3` | `RES_NU` |
| `[7]` | `16, 1/16, 1/15, 1/7` | `RES_R` (the axis the layer index walks) |
| `[8]` | `6360, 6420, 0, 0` | `Rg`, `Rt` |

Every axis slot is `(n, 1/n, 1/(n-1), 1/(n/2 - 1))`; every texture slot is `(w, h, 1/w, 1/h)`.
`SkyPrecomputePass.BuildSizeInfo` reproduces all 36 floats **bit-exactly**
(`VerifySizeInfoAgainstCapture` asserts it at startup and logs `EXACT`).

Two things worth noting:

- **The inscatter axes are not Bruneton's reference numbers** (`R=32, MU=128, MU_S=32, NU=8`).
  TotK uses a coarser `MU`/`R` table, so guessing would have produced a wrong-but-plausible LUT
  rather than an obviously broken one — which is why they were left unfilled until measured.
  `ResMuS * ResNu = 256` is the packed 3D width, corroborated independently by
  `sky_copy_inscatter`'s vertex shader computing `data[5].x * data[6].x`.
- **The two values that had been assumed came back exactly right**: `256×64` transmittance and
  `Rg`/`Rt` = `6360`/`6420`. Those were derived independently from the shader maths before the
  capture was read, so the agreement is a genuine check on the reconstruction.

The same capture also confirmed the `sky_postfx_sky` decode above against ground truth. Reading its
`RenderInfo` (`fp_c4`, 7 used vec4s = 112 bytes, exactly as the archive declares):

```
[0] 0.00411, 0.011275, 0.028364, 0.0018   <- betaR.rgb + betaM, matching SkyPostFx to 3 decimals
[2] 0.0092173, 0.67337, 0.73925, 0        <- unit length: the sun direction
[6] 0.5, 0.4, 0.3, 1                      <- GroundColor
```

`data[2].xyz` really is the sun direction and `data[6].xyz` really is the colour the LUT is blended
toward — both read off the ISA before the capture confirmed them.

## The chain is built and runs

`SkyPrecomputePass` runs all **twelve** real agl programs in Bruneton's iteration order:

```
transmittance -> irradiance(1) -> inscatter(1) [MRT: deltaSR+deltaSM] -> copy_inscatter(1)
  repeat N orders: delta_inscatter -> irradiance(N) -> inscatter(2) -> copy_irradiance/copy_inscatter(2)
-> bake_inscatter  ->  the 2D LUT the per-frame sky shader samples
```

The 3D LUTs (256×32×16, RGBA16F — the same allocation the real game uses) are written **slice by
slice**: the fragment shaders only read `gl_FragCoord.xy`, and take the altitude from `Config[2]`,
so each pass is dispatched once per R slice with `Config` rebuilt. `inscatter_step1` is the only
MRT pass. `copy_*` are the `+=` of the iteration and run with additive blending.

### Verified against the game's own LUT

The capture's `sky_bake_inscatter` draw samples the **finished inscatter table**, so it could be
pulled out whole (`renderdoc_read_texture`) and diffed against ours — a real ground-truth check,
not a heuristic:

| | result |
|---|---|
| mean per-slice correlation | **0.9492** (min 0.9002, max 0.9872) |
| channel ratio mine/real | R 1.166, **G 1.000**, B 0.770 |
| `SizeInfo` vs capture | **EXACT**, all 36 floats |
| `betaR` vs capture | **exact match** — `(0.00411, 0.011275, 0.028364)` |

That `betaR` matches the captured bytes exactly is worth noting on its own: it confirms Marrow is
reading the right authored values out of `master_field.baglsky`, independently of the chain.

**Scattering orders were measured, not assumed.** Sweeping against the real LUT gave correlations
of 0.9016 / 0.9392 / 0.9492 / 0.9523 at 2 / 4 / 6 / 8 orders, so the default is **6** (where the
curve flattens and green lands on 1.000), not Bruneton's reference 4.

### Two bugs the wiring surfaced (both fixed)

Worth recording because each produced a *plausible-looking* diagnostic while being completely wrong
on screen:

1. **Black sky.** `sky_bake_inscatter` was handed a `RenderInfo` containing only a layer index.
   That was wrong twice: it is a 2D pass over the whole 3D table so it has no layer at all (the
   layer convention belongs to `copy_inscatter`), and the block actually carries `betaR`/`betaM` at
   `[0]` - the weights that turn the Rayleigh and Mie tables into colour. With those zero the LUT
   came out with a perfectly healthy ALPHA range of [0,1] and identically **zero RGB**, so every
   range check passed while the sky rendered pure black. `BuildBakeRenderInfo` now starts from the
   captured block and overwrites only what is understood - the same pattern as `CloudUboBaseline`.
2. **Unanchored magnitude.** The LUT is in the game's own units and the pass paints into
   `targets.Final` *ahead* of `TonemapPass`'s Exposure multiply, so the raw table needs a scale. It is
   `AtmosphereIntensity * SceneGain * 0.6 * BgDifIntensity / 5`: the game multiplies the raw table by 1
   (`Context[13].x` in a capture), the lit path here is already scaled by `SceneGain`, and 0.6 is where this
   bake's noon zenith sits against the real `skybin` (high-sun zenith about 0.69, 1.08, 1.28). The table is
   not renormalised per palette any more: normalising its peak (the sun disc) darkened every other
   direction whenever a palette raised the Mie amplifier.

   Palette tint (`SkyPaletteTint`) defaults to 0, the game's behaviour. At 1 the table is multiplied by
   the fog colour's hue, which turned a blue noon sky green. The part of the view below the horizon is
   covered by `GroundPass` with the palette's hemisphere ground colour instead of whatever the table holds
   for rays into the planet.

The lesson for the range checks in `Verify()`: they cover RGB and alpha **separately** now.
Aggregate stats over all four channels hid a fully black LUT behind a healthy-looking [0,1].

### The open residual: resolved

`Config[1].x` is the Mie phase asymmetry. The solve passes read it as `g`, `g*g`, `2+g*g` and `1+g*g` (Cornette-Shanks), and the bake pass reads the
same constant from `RenderInfo[1].x`, which holds **0.75** in the captured block. It was left at zero, which made Mie scattering isotropic, so with a
palette's Mie amplifier of 12 the whole sky washed out. At 0.75 the sky is a clean gradient with the glare concentrated at the sun.

`SkyPrecomputePass.HorizonClampY` blends the table's rows near the horizon toward the row about 9 degrees above it. Those rows hold the longest light
paths, where blue is gone completely, and they drew a thin saturated yellow or red line along the horizon.

## What wiring the sky pass then requires

The per-frame pass is trivial. The blocker is **`cTexBakedInscatter`**, and there is no romfs file
for it — it is baked at runtime. Two routes:

1. **Run the real chain** (all the programs are extracted). Faithful, and it is genuinely a
   multi-pass GPU precompute with its own `Config`/`SizeInfo` blocks to reconstruct.
2. **Capture the baked LUT** from RenderDoc (see CLAUDE.md for the Ryujinx `--software-gui`
   workflow) and ship the sampling pass first. Faster to a visible result, but a captured LUT is
   *one atmosphere state* — the same trap that made the cloud dome "only work on some palettes"
   until its colours were re-sourced from EnvPalette per frame. Fine as a stepping stone to
   validate the sampling math; not an endpoint.

Note `sky_postfx_ground` is a **separate effect** — aerial perspective/fog composited over already
-rendered geometry (`cTexColor` + `cTexDepth`), not part of drawing the sky itself.
