# Uniform block notes

What the slots of the games' uniform blocks hold and why the renderer fills them as it does. The code keeps one-line comments; the evidence
lives here. A block is described by a `UboSpec` (shader name, binding, size), filled with a `UboWriter`, and bound through `GLResourceCache`.

| Block | Layout | Built by |
| --- | --- | --- |
| `gsys_context` (camera) | `Graphics/Ubos/GsysContext`, `TotkContextLayout`, `BotwBlocks` | `TotkCameraUniforms`, `BotwUniforms` |
| `gsys_environment` | `Graphics/Ubos/GsysEnvironment`, `TotkEnvironmentLayout`, `BotwBlocks` | `TotkLightingUniforms`, `BotwUniforms` |
| `gsys_scene_material` | `Graphics/Ubos/GsysSceneMaterial`, `TotkSceneMaterialLayout`, `BotwBlocks` | `TotkLightingUniforms`, `BotwUniforms` |
| `_Mtx` (bone palette) | `Graphics/Ubos/BonePalette` | `BonePalette` |
| cloud, sky, lens flare | `CloudBlocks`, `SkyPostFxBlocks`, `SkyPrecomputeBlocks`, `LensFlareBlocks` | the same classes |

## gsys_context

The camera slots are verified against the decompiled shaders:

- `NearFar` is (near, far, near/far, 1 - near/far).
- `DepthScale` is (1/(far-near), near/(far-near), aspect, 1/aspect).
- `FarMinusNear` is (far-near, 1/(1-near/far), 0, 0).
- `FieldOfView` is (aspect * tanHalfFovY, tanHalfFovY, fovY, 0). The `.xy` are view-ray scales (`out_attr1 = -2*x, 2*y`) and `.z` is the vertical
  FOV in radians (`sead::PerspectiveProjection::getFovy`, written by `calcGpuViewFlush`), read by chara_skin, chara_hair, chara_eye and others for
  cel-shading and normal reconstruction.
- `Resolution` is (screen width, screen height, texel width, texel height). The deferred vertex shader emits `out_attr2 = uv * this.xy * 0.5 + 0.5`
  and the fragment shader recovers the sub-texel fraction from it, which only works if `.xy` is the resolution in pixels.

TotK only:

- `ScreenSize` holds the size in pixels as floats in `.xy` and as integers in `.zw`, read by material_prog15554 (Zelda's face), the eye shaders and
  40-odd other G-buffer programs for 1D pixel coordinates (`y * width + x`). `field_hybrid`'s vertex stage reads the integers as a tile grid and draws
  one quad per instance, so a 1x1 grid is the whole screen in one instance.
- `FullscreenQuadParams` feeds the deferred vertex shader's quad generator: `gl_Position.x = (vid&1) * this.x * 2 - 1`, `y = 1 - (vid>>1) * this.y * 2`,
  and `z = this.z` when the material-id term is zero, so (1, 1, 0.5, 0) emits a full-screen quad from a four-vertex strip.

## gsys_environment

- `LightDirection0` is the direction the light travels. Every resolve pass evaluates direct lighting as `0 - dot(N, cLightDir0)`, so it is the negation
  of the direction toward the sun; its `.w` is `cLightIntensity0`, folded into the sun colour upstream.
- TotK writes world-space copies of the light directions. Only `LightDir0World` is read (by 100 shaders): the sky-island shadow samples
  `cTex_SkyIslandShadow` at `vec2(worldX - cLightDir0World.x * h, worldZ - cLightDir0World.z * h)`, walking the shading point back along the light by a
  height `h` (`SceneMat[28].w`). At zero every point is sampled directly overhead.
- The four fog groups stay zero. Enemy_MiasmaTentacle's Mt_Skin forward program (material_prog10336) reads `WorldFogMaskColor` (slot 16) and `FogFxColor`
  (slot 19) as a bare multiplier into its highlight colour, ungated by `.w`, so a placeholder skews it toward cyan. The Depths' palette
  (MainField_Underground) authors a FogColor of (0.001, 0.005, 0.001), so zero is a faithful stand-in and stays inert for the gated fog blend (see
  `env_slot47_research_request.md`).
- `Env[47].z = 1` makes the height-based ambient attenuation a no-op for any height (verified in chara_metal). `.x` and `.y` are inert: the one forward
  consumer (Mt_Skin's material_prog10336, `temp_348`) has the same z-protected shape, which at z = 1 collapses to `fma(temp_29, 0.5, 0.5)`.
  `temp_N` numbering is not stable across re-preparations, so re-verify by shape.
- `Env[70].z` nonzero collapses an alpha term to 0 rather than NaN; `.w` is unused. `Env[81]` is the volume-mask tint, driven by the palette's
  VolumeMaskColor and Intensity (0 is inert).
- A pow-exponent slot left at 0 becomes NaN when a forward shader evaluates `pow(0, 0)` through `exp2(log2(x) * k)`. 1 is NaN-free and, for the two
  fog-damp slots that are also used as curves, the correct "no curve".

## gsys_scene_material

- The defaults are the authored values of `Model/SystemModel.SceneMaterial`'s material, verbatim, in `TotkSceneMaterialDefaults`. Fields not listed are
  authored zero. The 88-float tail (576..927) is authored all zero except its last component (`.w = 1`) and stays zero: the entries with a confirmed
  reader (1, 16, 20, 54) are set, and the rest have no reader to justify a guess.
- `DynamicExposure`'s neutral is 1, not the authored 0, and 0 is not a no-op: several G-buffer shaders fold it into a lerp. Enemy_MiasmaTentacle's skin
  computes its emission scale as `fma(fma(volumeMask.y, -DynamicExposure, DynamicExposure), 0.5, 0.5)`, a flat 0.5 at 0 instead of 1.0 for an unmasked
  pixel. Daylight palettes author 1.0, which is the identity for every use seen, so this is an engine-style per-frame override (compare
  `game::gfx::ModelSceneExtension::setDynamicShadowParams`).
- `SceneShadingInfoExposure[1]` is added to the shadow term, `clamp(shadow + this, 0, 1)`: 0 lets PreShadow act, and 1 would unshadow the sun.
- Entries 16 and 20 are an ambient sky and ground pair the deferred vertex shader lerps by screen Y into an ambient term that prog 6 multiplies albedo by.
  Entry 54 is another pow exponent (see above).
- BotW's block is 96 bytes. Its first 64 bytes are the same fields as TotK's, and `ProcDiscardScales` at 68 holds the same three values as TotK's
  `ConstVanishing*` fields.

## Sky post-fx

The post-fx `Context` holds the view-ray basis `v = (ndc.x * [0].x, ndc.y * [1].y, -1)` in slots 0 to 2, and the camera-to-world rotation in slots 4 to 6,
dotted row-wise against `v` the way `BackgroundPass` does.

The adhoc fog's slots 10 and 11 were recovered by diffing which slots the `USE_ADHOC_FOG=1` variant newly reads:

```
master = sqrt(clamp([10].w * 4, 0, 1))                 (vertex -> in_attr1.x)
up     = clamp(viewDir.y, 0, 1)                        (0 horizon, 1 zenith)
scale  = mix([10].w, [10].z, pow(up, [10].y))
rgb    = mix(skyColour, [11].xyz, scale * master)
```

That is a colour that saturates at the horizon and thins toward the zenith, the haze band. The three scalars and the colour match
`adhoc_fog_atten_minscale_sky`, `adhoc_fog_atten_sky` and `adhoc_fog_color`.

In `RenderInfo`, `[6].w` is the shader's blend bias: it computes `clamp(lut.a + [6].w)`. 1 pins the weight so the table wins, as in the capture (the game
bakes per palette). One table is baked here, so lowering it lets the palette's colour through by exactly that amount; 0 is the game's behaviour.

## Cloud blocks

- `Common` is the capture of the game's block with every identified slot overwritten from live data. The slot of each layer field is the order the game's own
  fill routine (`FUN_7100de5c80`) writes them in. The noise offsets are speed times time: noise 1's two components land in slots 0.w and 1.x, noise 2's in
  1.y and 1.z.
- `[25].y` is the last factor the shader applies, so the brightness gain goes there; `[25].z` stays the unscaled reciprocal so the fog term scales the same way.
- Per-palette shading is colour times intensity in `.xyz` and the raw intensity in `.w`. Only the radiance terms carry the gain, because the shader reads `.w`
  structurally and scaling it would change the cloud's shape. The backlight colour has no intensity of its own, since `BacklightPower` is a separate term.
- The dome's direction matrix is a diagonal of the scale, the height above the camera and the scale, in the dome's own Y-up frame, which the shader also
  forwards as the surface's up-ness. The height is relative to the camera so the apex stays at the authored altitude, as the capture shows. The sun slot
  holds the direction the light travels.
- `[45].x` is zeroed so the distance fade decides alpha; a non-zero value there is a constant floor under it.
