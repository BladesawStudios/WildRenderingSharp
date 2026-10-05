# `gsys_environment` (Env, binding 6) — reverse-engineering notes

**Size:** 1328 bytes = 83 `vec4` slots. Slot `n` lives at byte `n * 16`.

The block has two halves with very different evidentiary status:

| Region | Bytes | Slots | How it was established | Status |
|---|---|---|---|---|
| Base | 0..415 | 0..25 | Read directly out of the game's own declaration code | **Solved** |
| Extension | 416..1327 | 26..82 | Inferred from how 286 decompiled shaders *use* each slot | **Partially recovered, this document** |

---

## 1. The base region is read, not guessed

`gsys::ModelRenderContext::initialize` (`0x7100e185cc`) makes exactly 35 consecutive
`agl::UniformBlock` declarations between `startDeclare(…, 0x23, …)` and `create(…)`, under the nvn
debug label `"gsys_env"`. Each declaration record is 12 bytes:

```
+0  u32   count          +4  u32  byte offset
+8  u16   components     +10 u8   type   (3=float, 6=vec3, 7=vec4, 0x13=blob)
```

Walking those 35 records lands exactly on byte 416, and every field name/offset agrees with
Splatoon 3's labelled `agl` source with zero mismatches. That is why slots 0..25 have real names in
`EnvUbo.Slots` rather than `UnknownNN`.

**The extension is not declared here.** 416 is where this function stops. TotK appends the
remaining 912 bytes somewhere else — `agl::UniformBlock::copyDeclarations` (`0x7100f5ce58`) is the
plausible mechanism, and the un-audited `startDeclare` callers are `FUN_7100c97b18` (x6),
`agl::pfx::Sky::Sky::initialize` (x2), `agl::fx::Cloud::initialize`,
`gsys::RenderBufferContext::initialize`, `FUN_7100e21940`, `FUN_7100e29aa8`, `FUN_7100e2c7a0`.
Until one of those is walked the same way, **everything in section 3 is inference from use, not a
recovered name table.**

## 2. Method for the extension, and its control

For every `fp_c9.data[N].c` / `vp_c9.data[N].c` read across all 286 extracted shaders, the
surrounding expression was captured and classified by the operation applied to it (`normalize` =>
direction, `exp2(log2(x)*k)` => `k` is a pow exponent, `fma(t, .x, .y)` + `clamp` => scale/bias
pair, and so on).

The already-solved base region is the control: the method independently calls slot 4 a direction and
slot 5 a colour, which is what the declaration table says they are. It also recovers the two decoded
fog groups' internal structure (3.1) correctly. That is the basis for trusting it on the extension —
and the reason every row below still carries an explicit confidence.

**43 of the 83 slots are read by at least one shader. Marrow writes 21.**

## 3. Findings

### 3.1 The fog-group template (control case — decoded region)

Slots 16..21 are two fog groups, and the shaders spell out the arithmetic:

```glsl
pow(clamp(sqrt(dist) * StartEndInv + Start, 0.0, 1.0), Damp) * Color
```

so `[17].w`/`[20].w` = `Start`, `[18].x`/`[21].x` = `StartEndInv`, `[18].y`/`[21].y` = `Damp` (the
pow exponent), `[16]`/`[19]` = colour with density in `.a`. This exact `clamp(fma(scale, bias))`
shape recurs all over the extension, which is what makes the extension slots readable at all.

> `Damp` at 0 is a live hazard, not a neutral: `pow(0, 0)` compiles to `exp2(log2(0) * 0)` =
> `exp2(NaN)`. `EnvUbo.PowExponentSlots` forces every such component to 1.0 for this reason.

### 3.2 `Env[23]` = `cLightDir0World` — was zero, now written

`Env[23]` is in the *solved* region, has a name, is read by **100 shaders (400 reads)** — and Marrow
left it at zero. Its one observed use pins the convention down:

```glsl
texture(cTex_SkyIslandShadow, vec2(worldX - Env[23].x * h, worldZ - Env[23].z * h))
```

(`h` = `SceneMat[28].w`.) It walks the shading point *back along the direction light travels* by a
height, to find where that point sits underneath a sky island — which only works if this is the
world-space counterpart of `cLightDir0`, sharing its "direction of travel" sign convention. At zero
the walk-back distance was always zero, so every point sampled the shadow map directly overhead.

Fixed: `EnvUbo.BuildFromLighting` now takes `sunDirWorld` and writes slots 22/23/24. (22 and 24 have
no readers in the extracted set; they are written because it is free to be right.)

### 3.3 Extension slots, by inferred role

Confidence: **High** = the expression determines the meaning almost uniquely. **Med** = the shape is
unambiguous (e.g. "scale/bias into a clamp") but *what* it fades is not proven. **Low** = too few
readers or too little structure to commit.

#### Range fades — `clamp(fma(x, scale, bias), 0, 1)` pairs

| Slot | Byte | Comp | Readers | Representative use | Inferred | Conf. |
|---|---|---|---|---|---|---|
| 26 | 416 | xy | 107 | `1 - clamp(fma(sqrt(d), .x, .y))` | distance fade (inverted) | Med |
| 28 | 448 | xy | 107 | `log2(1 - clamp(fma(sqrt(d), .x, .y)))` | distance fade feeding a pow | Med |
| 44 | 704 | xyzw | 106 | two independent `clamp(fma(t, .x, .y))` | two fade pairs | Med |
| 49 | 784 | xy | 100 | `clamp(fma(sqrt(d), .x, .y))` | distance fade | Med |
| 64/65 | 1024/1040 | xy / xyzw | 6 | `max(distFade, heightFade)`, `[65].z` = height origin | combined distance-or-height fade | Med |
| 67 | 1072 | xz | 1 | `clamp((sqrt(d) - .x) * (1/(.z - .x)))` | explicit start/end range | Med |
| 82 | 1312 | xz | **98** | `(t - .x) * .z`, applied iteratively | range remap (start, 1/range) | Med |

All of these are **neutral at zero** (scale 0 => the clamp collapses), except `Env[67]`, where
`1/(.z - .x)` divides by zero and saturates the fade to 1 (NaN only at exactly `d == 0`). One
reader; recorded, not chased.

#### Falloff curves

| Slot | Byte | Comp | Readers | Representative use | Inferred | Conf. |
|---|---|---|---|---|---|---|
| 27 | 432 | xz | 107 | `.z * (1 - pow(base, .x))` | strength `.z`, exponent `.x` | High |
| 29 | 464 | xzw | 107 | `fma(exp2(t*.z), -.w, 1) * pow(1-t, .x)` | exp rate `.z`, amount `.w`, exponent `.x` | High |
| 51/57 | 816/912 | xyz / xyzw | 10 / 102 | `pow(abs(1 - abs(N·V)), [57].w) * [51].y` | **rim light**: `[51]` colour, `[57].w` power | High |
| 56 | 896 | xyz | 102 | `fma(t, [56].z, [51].z)`, `t*0.5*[56].x` | scales paired with `[51]` | Low |

The `.x`/`.w` exponents in 27/29/57 are already forced to 1.0 by `PowExponentSlots`; the *strengths*
(`[27].z`, `[29].w`, `[51].xyz`) are genuinely neutral at zero.

#### Texture-driven effects — these name themselves

| Slot | Byte | Comp | Readers | Use | Inferred | Conf. |
|---|---|---|---|---|---|---|
| 45 | 720 | xyzw | 106 | `texture(cTex_MinusFieldDarkness, vec2(fma(u,.x,.z), fma(v,.y,.w)))` | **UV scale `.xy` / offset `.zw`** for the Depths darkness map | High |
| 46 | 736 | xyz | 106 | `mix(…, [46].y, [46].z)`, `1 - [46].x` | blend weights for the above | High |
| 68 | 1088 | w | 107 | scales the `cTex_DeferredCloudNoise` result | **cloud-shadow intensity** | High |
| 69 | 1104 | xyz | 107 | `clamp(fma(noise + .z, .y, .x))` | cloud-noise remap (offset/scale/bias) | High |
| 32/33 | 512/528 | xyzw / xyz | 6 (vertex) | `texture(TexWindSwell, vec2(fma(p.z, -.z, fma(p.y, -.y, …))))` | **wind sample basis** — two rows of a wind matrix | High |

#### Additive colours

`Env[30]` (480, xyzw, 107 readers) and `Env[68].xyz` are summed into `out_attr1` in the preshading
pass alongside the two decoded fog colours `Env[16]` and `Env[19]`:

```glsl
out_attr1 = fma(t, Env[30], fma(t, …, fma(t, Env[68], fma(t, Env[16], t * Env[19]))))
```

Being weighted members of the same sum as two *known* fog colours makes them **additional
atmospheric colour terms** with high confidence — and neutral at zero, which is why Marrow renders
without them.

#### Direction

`Env[43]` (688, xyz, 100 readers): `dot(v, Env[43].xyz)`, then squared. An axis projected and
squared — most plausibly a wind or anisotropy axis. A zero-length vector makes the term 0, so it is
neutral. **Med.**

#### Insufficient evidence

`Env[31]` (1 reader), `[42]` (2), `[48]` (5), `[74]`/`[75]` (3 — `lerp([75], [74], t)`, so a colour
or value pair), `[76]`/`[77]` (1). Recorded for completeness; not decoded.

## 4. Why Marrow renders at all with 22 slots zeroed

Because the extension is overwhelmingly built from **scale/bias fade pairs, effect strengths, and
additive colour terms**, and zero is the correct "off" value for all three. The genuine exceptions
are exactly the two classes this work found and fixed:

1. **Pow exponents**, where `0` means `pow(x, 0)` => `exp2(log2(0)*0)` => NaN, not `1`. Handled by
   `EnvUbo.PowExponentSlots`.
2. **Directions**, where `0` is not a no-op but a degenerate vector. `Env[23]` was the one such slot
   with real readership; now written (3.2).

The remaining zeroed slots suppress effects Marrow does not implement anyway — Depths darkness,
cloud shadows, sky-island shadows, wind — each of which also needs a texture Marrow does not bind.
**Populating them without those textures would not improve the image**, which is the honest reason
to stop here rather than guess at values.

## 5. To finish the job properly

Walk the remaining `startDeclare` callers listed in section 1 the same way section 1 walked
`gsys::ModelRenderContext::initialize`. That converts every Med/Low row above into a real name and
type. Inference from use can say "this is a scale/bias pair into a clamp"; only the declaration
table can say *which* field it is.
