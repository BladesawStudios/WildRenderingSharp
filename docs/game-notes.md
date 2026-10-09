# Game notes

Reverse-engineering findings and format history that used to live in XML doc comments. Addresses are Ghidra offsets in the shipped TotK binary.

## WildRenderingSharp.Preparation/ModelPreparer.cs

**`public static void EnsureCloudTextures(string romfsRoot, string systemTexturesDirectory)`**

Superseded by `TotkCloudMasks`, which reads the real assets (below). The old name-guess extraction and the captured BC4 copies are gone.

**`public static bool IsUpToDate(string romfsRoot, string dataDirectory, IEnumerable<string>? modRomfsLayers = null)`**

False if the prepared model was built from different romfs files than the current root and mod set would supply: a mod toggled that touches one of
its files, or a mod file edited. Only files the prepare looked up are checked, so toggling an unrelated mod costs nothing.

A cache predating this check has no stamp and is trusted only while no mods are active. A stamp from an older `PreparationVersion` is never up to date.

## WildRenderingSharp/Assets/AnimCurveManifestEntry.cs

**`public sealed class AnimCurveManifestEntry`**

One curve from a `BoneAnimManifestEntry` - a straight transcription of
`BfresLibrary.AnimCurve`'s already-decoded (float, regardless of on-disk compression)
`Frames`/`Keys`, plus `TargetOffset` (`AnimCurve.AnimDataOffset`)
to say which component of the bone's TRS this curve drives:
4=ScaleX, 8=ScaleY, 12=ScaleZ, 16=TranslateX, 20=TranslateY, 24=TranslateZ,
32=RotateX, 36=RotateY, 40=RotateZ, 44=RotateW.

Those offsets are not guessed - they are the byte offsets
`nn::g3d2::SkeletalAnimObj::ApplyToImpl` (Ghidra 0x710007a1a0) reads out of its per-bone
result struct when it copies scale into the local matrix's scale slot, translation into its
translation row, and rotation into the Euler/quaternion conversion.

**(member)**

The curve's BASE value - `AnimCurve.Offset`, the field at `ResAnimCurve[0x24]`.
`nn::g3d2::ResAnimCurve::EvaluateFloat` (Ghidra 0x7100073d90) finishes every curve
with `Offset + raw * Scale`, where `raw` is the (usually quantized-integer)
polynomial the Cubic/Linear/Baked evaluator produced. Do not confuse this with
`Delta`.

## WildRenderingSharp/Assets/BoneManifestEntry.cs

**`public static Matrix4x4 EulerXyzToMatrix(float x, float y, float z)`**

Euler XYZ radians to a row-vector rotation matrix: `Rx * Ry * Rz` (apply X, then Y,
then Z). Confirmed against `nn::g3d2::SkeletalAnimObj::ApplyToImpl<nn::g3d::EulerToMtx>`
(Ghidra 0x710007a1a0) and `SkeletonObj::ClearLocalMtx` (0x7100ad6978), whose first
output row is `(cy*cz, cy*sz, -sy)` - the row 0 of exactly this product, and NOT of the
reverse order.

## WildRenderingSharp/Assets/MaterialUniformEntry.cs

**``**

Whether this shape's own real decompiled shader (G-buffer/Z-only/forward, whichever exist)
actually references this parameter - null when unknown (an older cache predating this
field, or `Type` is itself null). A live editor should treat null the same as
true (show it) rather than hide anything it isn't certain about.

## WildRenderingSharp/Assets/SamplerBinding.cs

**``**

The container's per-texture channel-selector bytes (source channel per output, R/G/B/A order: `0=R, 1=G, 2=B, 3=A`, with `4/5` meaning constant 0/1 on other Nintendo containers, not observed
in this game's TXTG data). Applied in `TextureCache.ApplySwizzle`, whose remarks explain why an earlier Ghidra-derived encoding was reverted. Null when the manifest predates the field or the TXTG
could not be read, so the caller falls back to the format-based heuristic instead of applying a wrong swizzle. `int[]`, not `byte[]`, because `System.Text.Json`'s `byte[]` converter
expects a base64 string.

**`}`**

The authored GX2 wrap mode for this sampler's U/V axes (`GX2TexClamp`'s names: "Wrap", "Mirror", "Clamp", "ClampBorder", "ClampToEdge", ...; see `ExportManifest.BuildSamplers`), applied in
`TextureCache.MapWrapMode`. Hardcoding GL_REPEAT made textures the game clamps (an eye iris scrolled by a texture-SRT anim) tile once animated UVs left 0..1. Null on a manifest prepared before
the field existed, which falls back to Wrap.

## WildRenderingSharp/Assets/ShapeManifestEntry.cs

**``**

Every level of detail in `IndexFile`, finest first, as `[first index, count]`.
LOD 0 is always `[0, IndexCount]`, and the coarser ones follow it in the same file. Null
for a model prepared before levels of detail were exported - it has only LOD 0.

## WildRenderingSharp/Assets/SkeletalAnimManifest.cs

**`/// <summary><c>SkeletalAnim.FlagsScale</c>, same encoding as <see cref`**

`SkeletalAnim.FlagsRotate`. `nn::g3d2::SkeletalAnimObj::ApplyTo` (Ghidra
0x710007a93c) dispatches on `(FSKA.flags >> 12) & 7`: 0 takes the quaternion
path, 1 takes `ApplyToImpl<nn::g3d::EulerToMtx>`. When this is false,
`BaseRotate` and curve target offsets 32/36/40 are Euler
XYZ RADIANS and the W slot is unused - reading them as a quaternion produces a normalized
garbage rotation, not a slightly-off one.

Defaults to true only so an anim manifest exported before this field existed keeps its old
behaviour; every such file should be re-exported.

## WildRenderingSharp/Assets/SkeletonManifest.cs

**`public sealed class SkeletonManifest`**

Deserialized `<Model>.skeleton.json`: everything needed to build the `BonePaletteUbo` at bind pose (via `BindPoseWorldMatrices`)
or at an animated pose given a `SkeletalAnimManifest`. The palette has two segments and `MatrixToBoneList` covers both (`SmoothCount` +
`RigidCount` entries), while `InverseModelMatrices` is parallel to the smooth prefix only; see `BonePaletteUbo` for the Ghidra citations.

**`/// <summary>Length of the palette's smooth segment (<c>FSKL[0x3A]</c>), or -1 in a manifest exported before this field existed - read <see cref`**

See `SkeletonScalingMode`. Defaults to `Standard` for a manifest exported before this field existed - the mode that matches the old unconditional walk.

**`/// <summary>Length of the palette's rigid segment (<c>FSKL[0x3C]</c>), or -1 if not exported - read <see cref`**

Length of the palette's smooth segment (`FSKL[0x3A]`), or -1 in a manifest exported before this field existed - read `SmoothCount` instead, which derives it in that case.

**`public int SmoothCount`**

The length of the palette's smooth segment, clamped to what `MatrixToBoneList` can supply. A manifest exported before `SmoothMatrixCount` existed gets it counted off the bones' `SmoothMatrixIndex`, the same number, which also splits those manifests correctly.

## WildRenderingSharp/Assets/SkeletonScalingMode.cs

**`public enum SkeletonScalingMode`**

How a skeleton's bone scales propagate down the hierarchy - `(FSKL.flags >> 8) & 3`, exactly the value `nn::g3d2::SkeletonObj::CalculateWorldMtx` (Ghidra 0x710008246c) switches its three `CalculateWorldImpl` specialisations on.

**`None`**

`CalculateWorldImpl<CalculateWorldNoScale>` (0x7100080b40) - bone scale is never applied at all.

**`Standard`**

`CalculateWorldImpl<CalculateWorldStd>` (0x7100080d00) - scale multiplies world rows 0/1/2 after the parent multiply, and cascades to children like any other part of the transform.

**`Maya`**

`CalculateWorldImpl<CalculateWorldMaya>` (0x7100080f2c) - Standard, plus segment scale compensate for any bone flagged with it.

**`Softimage`**

No dedicated specialisation exists in the shipped binary (the dispatch table at 0x71041da970 runs out at Maya); treated as `Maya`.

## WildRenderingSharp/Assets/TexturePatternAnimManifest.cs

**`public sealed class TexturePatternAnimManifest : IAnimClip`**

Deserialized `<Model>.<AnimName>.texpat.json`, written by
`ShaderLibrary.CompileTool.ExportTexturePatternAnim` from a BFRES texture pattern anim
(an FMAA with `TexturePatternCount > 0`).

A texture pattern anim does not move or shade anything - it re-points a material's SAMPLER at a
different texture per frame. See `ExportTexturePatternAnim` for the Ghidra citations behind
that; `TexturePatternPose` is the runtime that applies it.

## WildRenderingSharp/Assets/TexturePatternSamplerEntry.cs

**`public int Evaluate(float frame)`**

The texture index at `frame`. STEP, not interpolated: the value of the
last key at or before the frame is held until the next key. That is exactly what the
shipped evaluator does - `ResAnimCurve::EvaluateInt` (Ghidra 0x71009b774c) dispatches
to the step function at 0x7100073a68, which returns `keys[FindFrame(frame)]` with no
blend of any kind, and adds the curve's integer `Offset` (already folded into
`Values` at export).

## WildRenderingSharp/Profiles/Totk/Atmosphere/CloudPostFxLayer.cs

**`public sealed class CloudPostFxLayer`**

One `CloudParamN` block of `postfx/master_field.baglclwd`: the per-layer parameters of the `agl::fx::Cloud` billboard-dome shading model, every field (see
`WildRenderingSharp.AampReader.SkyPostFxJson.ParseObject`, which dumps the object generically). Names match the AAMP names exactly, including the "m" prefix and the
authored typo "Distotion", so they cross-reference the decompiled `agl_cloud.vert`/`.frag` and its uniform reflection without a mapping table.

`CloudParam2` is byte-identical to `CloudParam1`; `CloudPostFx` carries all three as the baseline the weather data overrides. The `*No` and `*No_Blend` fields are slot indices into the cloud's
texture table (defaults 0, 2, 1, 1 for base, blended base, noise, blended noise).

## Cloud dome masks

`cBaseTexture`, `cNoiseTexture` and their blend slots are three 512x512 BC4 textures in the BNTX at offset 0x1000 of `collect.genvres`, inside
`Env/GameScene.Nin_NX_NVN.genvb.zs` (a SARC; the zstd needs the game's dictionary). `FUN_71010865ec`, registered as a `ModelSceneExtension`
callback (hash 0x11247be8) that runs after the env binary loads, copies them into the cloud's table (`Cloud+0x5688`, stride 0x10, count at `Cloud+0x5780`)
in this order: slot 0 `cloudtexture03` (the wispy base), slot 1 `cloudtexture02` (soft round blobs, tileable), slot 2 `cloudtexture04` (the base pattern on a
different tone curve). They are byte-identical to a GPU capture of a real cloud draw once deswizzled. `mUseProcedualTexture` is false, so none of the
`noise_*` programs in `agl_technique_proc.sharcb` are involved; they have no CPU callers for the cloud.

The renderer binds the indices the layer names (the weather's, see below).

The masks have a full 10-level mip chain and the shader's base samples read `.xw`; the BNTX channel select is red for every channel, so
R8 with an RRRR swizzle is exact. Sampling without the mips turned the far end of the dome into speckle.

`TexToGo/cloud_noise.txtg` (64x64 BC4) is unrelated: the scene material samples it as `cTex_DeferredCloudNoise` for cloud shadows on terrain.

## Cloud weather

`agl::fx::Cloud` has three layers (struct stride 0x1298 in the object; `FUN_71009548e4` draws layer N, skipping it if it is disabled or its `AlphaMul` is 0 or less).
`WorldEnvMgr::applyEnvironment` (0x7100b42e84) fills each from three sources, all in `Pack/Bootup.Nin_NX_NVN.pack.zs`:

- `WorldMgr/PrequelPrCloud/00N` (one per layer): the texture indices (0, 2, 1, 1 for base, blended base, noise, blended noise), `BaseTexScale`, `ScrollSpd`,
  the four `NoiseAdd*` terms and the altitude limit. Layer 1 has `EnableLimitAltitude` from 900 to 950, so it only appears once the camera is above 900.
- `WorldMgr/PrequelCwCloud/NNN_L` (weather NNN, layer L; only layers 0 and 1 have files): `AlphaMul`, `AlphaThreshold`, `Density`, `Distotion`, `SkyHeight`, the
  backlight, emboss, highlight and far-fade fields. A value with `Min`, `Max` and `SinSeedAdd` and no `_IsUseBase: true` breathes between the two on
  `(sin(phase) + 1) / 2`, the phase advancing by `SinSeedAdd` a frame; `FUN_7100b4bc00` is the evaluator. Layer 2 has no file and is not drawn.
- `PrequelSkyPalette` (eight per layer) and `PrequelCloudPat` (event overrides) replace some of the same fields when the game turns them on; the renderer does not
  apply either.

`CloudWeather` reads the first two, `CloudLayerResolver` lays them over `CloudParamN`, and `TotkSettings.CloudWeatherSet` picks the file number (the capture below
is weather 0). Wind and the sine run on a 30 frames a second clock.

The fill routine (`FUN_7100de5c80`) writes the layer's floats into the Common block in a fixed order, which settled the slots (see `CloudDomePass.BuildCommonBlock`):
0.x time, 0.y `Distotion`, 0.z `Density`, then the four noise offsets, the noise and emboss fields, `AlphaMul / (1 - AlphaThreshold)` in 4.y, the threshold in 4.z,
`BacklightPower`, `BacklightRange`, `BacklightParam0` and `BacklightParam1` in 4.w to 5.z, and `BaseTexScale` in 5.w (the shader divides by it). With weather 0 and
`PrequelPrCloud/000`, every compared slot of a capture of the game's cloud draw comes out equal, the animated ones lying inside their Min and Max.

The noise offsets are accumulated speed times time, with the speed `wind.x * NoiseAdd + wind.y * NoiseAdd_side` (and the side term first for the other axis). The
capture's four offsets (0.436, -0.083, -1.034, 1.299) give a wind of (0.442, -0.897) and one multiplier; its scroll offsets (-0.0384, 0.0782) solve to the same wind.
Those two multipliers (1.74e-4 and 8.73e-5 a second) and the wind are what `CloudLayerResolver` and `TotkSettings.CloudWind` use: the game's own wind state is computed
elsewhere and was not found. The 30 frames a second is likewise an assumption; the range check is all it has been tested against.

## WildRenderingSharp/Profiles/Totk/Deferred/KnownMaterialFixes.cs

**`public sealed class KnownMaterialFixes : IDisposable`**

Manual, individually verified corrections for game rendering behaviour the shader-driven pipeline cannot derive; see `EnableKnownMaterialFixes`.
Each fix is scoped to one named game asset and verified against observed behaviour, never a heuristic that could reach other materials. To add one: confirm the behaviour by
observation, exhaust static analysis (shader logic, material data, variant resolution, the game's option-resolution code via Ghidra), then add a narrow correction with the
same evidence trail.

## WildRenderingSharp/Profiles/Totk/Sky/CloudDomeMesh.cs

**`public static class CloudDomeMesh`**

Generates the real `agl::fx::Cloud` dome mesh procedurally - traced from the real
`agl::fx::Cloud::initVertex_` decompile (Ghidra), not an invented approximation. The real
game never loads a mesh file for this: it builds a local-space unit dome in code, 24 vertices
per ring (a fixed constant - confirmed as the real angle step read straight from the game's own
data segment, 0.2617994 rad = 2*pi/24 = 15 degrees) around a caller-supplied ring count (the
real game builds two LOD instances from the same code, one with 12 rings and one with 24 - see
the two `initVertex_` call sites in `Cloud::initialize`), plus one apex vertex that
closes the top.

Per ring `i` (0 = outermost/rim ring, working inward):

```
t = i / rings
radius = 1 - t^3                          // 1 at the rim, tapering toward 0 near the apex
circleHeight = sqrt(1 - radius^2)         // standard unit-circle height for that radius
height = circleHeight <= 0.1
    ? (0.1 - circleHeight) * -0.3 + 0.1   // flattens the dome near the rim/horizon
    : circleHeight
y = height - 0.07                         // shifts the rim exactly to y=0
vertex(radius*sin(angle), y, radius*cos(angle))  for angle = j * (2*pi/24), j = 0..23
```

then one final apex vertex at `(0, 1, 0)`.

The four magic constants (-0.07, 0.1, -0.3, 2*pi/24) are the real game's own values, read
directly out of the game binary's data segment via Ghidra - not tuned or guessed. The
rim-flattening branch is a real, deliberate design choice: without it the dome would be a plain
hemisphere with a hard vertical wall at the horizon; the real game instead ramps the last ~10%
of height smoothly, giving a shallow "false horizon" the sky/cloud blend can fade into.

Winding/index order was NOT traced (`Cloud::initIndex_` wasn't read, only
`initVertex_`) - `BuildIndices` assumes counter-clockwise; flip it if backfaces
show up.

## WildRenderingSharp/Profiles/Totk/Sky/SkyPostFxPass.cs

**`public static AdhocFog Resolve(EnvPalette palette, SkyPostFx postfx, float skyIntensity, float strength,`**

Builds the fog parameters for a palette, in the sky's own HDR units.

Verified against a capture of the game's own sky draw, where Context reads
`[10] = 4.0, 0.5, 0.3, 0.089538` and `[11] = 0.585, 1.0, 0.806, 1.0`. `[11].xyz` is
`Prequel_MainField_Bluesky_3_Noon`'s `FogColor` bit-exactly, which confirms the slot and that the
colour goes in raw. `[10].x` is `adhoc_fog_atten_grd` (the ground pass's parameter, unread here)
and `[10].z` is `adhoc_fog_atten_minscale_sky`, both matching `master_field.baglsky`.

Two values come from the capture rather than the files. `[10].y` is 0.5, not the file's
`adhoc_fog_atten_sky` (0.764) nor the palette's `AfParam_attenuationForSky` (0); its CPU-side
derivation is unknown. And `[10].z` is the minscale raw, not scaled by the density.

The shader mixes from `[10].w` at the horizon to `[10].z` at the zenith. At noon 0.0895 < 0.3,
so the pale fog sits mostly overhead; under a blood moon the palette authors `FogColor` alpha 0.6
> 0.3, so the mix runs the other way and saturated colour piles at the horizon. One formula gives both looks.

The density (`[10].w`) derivation is unconfirmed. The palette's `FogColor` alpha is clearly it
where authored, but noon authors 0 while the capture shows 0.089538, so a floor comes from somewhere
unfound. Taking the larger of the two reproduces the noon frame and gives a blood moon its band; it
reconciles the evidence rather than deriving it.

## WildRenderingSharp/Graphics/BonePaletteUbo.cs

**`public sealed class BonePaletteUbo : IUboBlock`**

TotK's skinning matrix palette (the engine's `g3d_SkeletonUniformBlock`, the shader symbol `_Mtx`), binding 2. 48 bytes per matrix: a mat3x4 of three vec4
rows, row-vector convention (a vertex is `v * M`; composition is "apply the first operand, then the second"; see `Mat4Math.Multiply` and
`SkeletonPose`). The vertex shader unpacks two 16-bit bone indices per blend-index float and indexes this block directly, so an index is a plain array index
into `Build`'s output, not a byte offset.

Layout recovered from the game via Ghidra. `nn::g3d2::SkeletonObj::SetupBlockBufferImpl` (0x7100081dfc) sizes the buffer as `(smoothCount + rigidCount) * 0x30`
from two ushorts at `FSKL[0x3A]` and `FSKL[0x3C]`. `SkeletonObj::CalculateSkeleton` (0x71000824a8) fills it in two segments, both reading the same
bone-index array (`FSKL[0x18]`, `MatrixToBoneList`): slots [0, smoothCount) hold `InverseModelMatrix[i] * BoneWorld[MatrixToBoneList[i]]`, stepping a
0x30-stride inverse-bind array (`FSKL[0x20]`) in lock step; slots [smoothCount, smoothCount + rigidCount) hold `BoneWorld[MatrixToBoneList[smoothCount + j]]`,
transposed into mat3x4 rows and copied directly with no inverse-bind multiply.

The split is easy to get wrong. `MatrixToBoneList` is one combined array and `InverseModelMatrices` holds only smoothCount entries, so the inverse-bind list
being shorter is normal, not a truncated file. Treating the whole list as smooth applies an invented inverse-bind to every rigid slot and pushes every later index past
the real palette.

A vertex's `vBoneIndices` is an absolute slot into the combined array: `vertex_skin_count >= 2` lands in the smooth segment and
`vertex_skin_count == 1` carries the bone's `RigidMatrixIndex`, which BFRES already stores offset past the smooth segment. Nothing adds smoothCount a second
time (see `ShaderLibrary.CompileTool.ExportTestBench` for the export side).

## WildRenderingSharp/Profiles/Totk/Ubos/SceneMatUbo.cs

**`public static SceneMatUbo BuildFromLighting(`**

Starts from the authored defaults for all 88 fields, then overlays the few the renderer drives from live lighting state.

The defaults come from a real model: `gsys::ModelScene::initialize_` loads `Model/SystemModel.SceneMaterial.bfres.mc` through `gsys::ModelNW::initialize` and
passes it into every render context's `setSceneMaterial` (found via Ghidra). Unlike Context and Env, which engine code writes by name every frame, most fields here,
especially every "Const" one, are that model's ("MasterMaterial") authored `ShaderParams`, read with `ShaderLibrary.CompileTool --dump-scene-material` (see
`BuildMaterialUbo.DumpSceneMaterial`) by the same name-join as a material's own block. All 88 matched by name.
This is what explains the Zonai "Blueprint" cyan bug: every `ConstBlueprint*` colour is a shade of green (e.g. `ConstBlueprintEmissionColor` =
(0.01, 1, 0.2)), and leaving them zero made the formula collapse to a negative result, which produced the pure cyan on Enemy_MiasmaTentacle's Mt_Skin once its forward
program ran (that material authors `p_blue_print_alpha = 1.0`).

## WildRenderingSharp/Rendering/AnimCurveEval.cs

**`public static class AnimCurveEval`**

The one implementation of BFRES curve evaluation, shared by every kind of animation WildRenderingSharp
plays - skeletal TRS curves, texture pattern index curves and shader parameter curves all come
out of the same `ResAnimCurve` and are evaluated by the same two entry points in the game.

Reverse engineered from the shipped code, not guessed:
  `ResAnimCurve::EvaluateFloat` (Ghidra 0x7100073d90) finishes with
    `Offset + raw * Scale`, reading `ResAnimCurve[0x24]` as a FLOAT;
  `ResAnimCurve::EvaluateInt` (0x71009b774c) finishes with `Offset + raw` - the same
    field read as an INT, and with NO Scale multiply at all;
  `EvaluateCubic<float>` (0x71000733b4) treats the 4 keys per segment as already-baked
    polynomial COEFFICIENTS, not Hermite value/tangent pairs;
  `EvaluateLinear<float>` (0x71000736d0) uses the same normalized t, so key[1] is the
    whole segment's delta rather than a per-frame slope;
  `EvaluateBakedFloat<float>` (0x7100073984) has no frame list - one value per integer
    frame, blended by the fractional part;
  the step evaluator behind `EvaluateInt` (0x7100073a68) returns `keys[FindFrame(frame)]`
    with no interpolation of any kind;
  `FindFrame<float>` (0x710007316c) is the segment search all of them share.

Frames outside `[startFrame, endFrame]` are CLAMPED here. The game additionally supports
repeat/mirror/relative-repeat wrapping (the pre/post wrap bits of `ResAnimCurve[0x10]`),
which only differ outside an anim's own range - and WildRenderingSharp's playback already keeps the frame
inside it.

## WildRenderingSharp/Rendering/MaterialAnimPose.cs

**`public static class MaterialAnimPose`**

Applies shader parameter animations by rewriting the affected materials' `gsys_material`
uniform blocks - which is exactly what `nn::g3d2::MaterialAnimObj::ApplyTo` (Ghidra
0x7100080894) does: evaluate each curve, then copy the resulting 4-byte word into the material's
parameter block. The only translation WildRenderingSharp adds is turning the anim's (parameter NAME, byte
within parameter) address into a block offset, via the layout sidecar
`BuildMaterialUbo.WriteParamLayout` exports.

Several anims can be applied at once (a colour anim and a texture-SRT scroll on the same
material, say). `Apply` takes them together and rebuilds each material's block ONCE
from its untouched baseline, so a later anim overwriting an earlier one's parameter behaves the
same as the engine's own sequential ApplyTo, and dropping an anim restores the baseline rather
than leaving the last value it wrote.

## WildRenderingSharp/Rendering/SkeletonPose.cs

**`public static class SkeletonPose`**

Walks a `SkeletonManifest`'s hierarchy into per-bone world matrices, at bind pose or (given a `SkeletalAnimManifest` and a frame) at an animated pose.

`World` reproduces `nn::g3d2::SkeletonObj::CalculateWorldImpl`, whose three specialisations `CalculateWorldMtx` (Ghidra 0x710008246c) selects with
`(ResSkeleton.flags >> 8) & 3`, i.e. `ScalingMode`. `None` (0x7100080b40) never applies local scale.
`Standard` (0x7100080d00) is `world = Scale * Rotate * Translate * parentWorld` in row-vector order; the game multiplies world rows 0/1/2 by
scale.x/y/z after the parent multiply and leaves the translation row alone, the same matrix as scaling first here. `Maya` (0x7100080f2c) adds
segment scale compensate: a bone with flag bit 23 first divides its parent's world basis rows by the parent's own local scale, so the parent's scale does not cascade into it
(skipping it stretches a non-uniform rig; the game skips the divide when the scale is already 1, reproduced only because a zero component must not divide).
`Softimage` has no specialisation and is treated as Maya.

`EvaluateCurve` is reverse engineered from `nn::g3d2::ResAnimCurve::EvaluateCubic<float>` (0x71000733b4), `EvaluateLinear` (0x71000736d0),
`EvaluateBakedFloat` (0x7100073984) and `FindFrame` (0x710007316c). Cubic keys are baked polynomial coefficients, not Hermite value and tangent pairs: with
`t = (frame - Frames[i]) / (Frames[i+1] - Frames[i])`, `raw = Keys[i][0] + Keys[i][1]*t + Keys[i][2]*t^2 + Keys[i][3]*t^3`. Linear is
`raw = Keys[i][0] + Keys[i][1]*t` with the same normalised t, so `Keys[i][1]` is the segment's whole delta. BakedFloat has no frame list: one value per integer
frame, blended by the fractional part. Every type finishes with `value = Offset + raw * Scale` (`EvaluateFloat`, 0x7100073d90, reading `ResAnimCurve[0x24]` and
`[0x20]`); Offset, not the Delta field at 0x28 that only relative-repeat wrapping uses.

**`public static float EvaluateCurve(AnimCurveManifestEntry curve, float frame)`**

Delegates to `AnimCurveEval`, shared by every kind of animation played; the maths and its Ghidra derivation live there.

## WildRenderingSharp/Rendering/TexSrtBake.cs

**`public static class TexSrtBake`**

Bakes an authored TexSrt (mode, scaleX, scaleY, rotation, translateX, translateY) into the 2x2 rotate-scale matrix plus translation the compiled shader's `gsys_material` block stores. It is the
runtime twin of `ShaderLibrary.CompileTool.BuildMaterialUbo.TexSrtBake`, whose remarks carry the Ghidra derivation (`nn::g3d2::MaterialObj::ConvertDirtyParams`'s per-kind callback table,
dispatcher 0x7100072448, mode 0 baker 0x7100072860, mode 1 baker 0x7100072950). Duplicated rather than shared because this library takes no dependency on the offline BFRES and BFSHA tooling, and it
is pure float math.

`MaterialAnimPose` needs it because a material-parameter animation can drive just one sub-field of a TexSrt (a scroll touching only translateY): the untouched
sub-fields come from the material's authored baseline (`MaterialUniformEntry.RawSrt`) and all six values are re-baked together every frame, as the offline overlay does at export. Writing an
animated curve's raw float into the already-baked buffer only looks right at the identity baseline (scale 1, rotation 0, where the pivot terms cancel) and is wrong for any real scale or rotation.

## WildRenderingSharp/Rendering/TexturePatternPose.cs

**`public static class TexturePatternPose`**

Applies a `TexturePatternAnimManifest` to a loaded model for one frame, by writing
each affected shape's `SamplerOverrides`. Nothing else in the pipeline
needs to know a pattern anim exists: `ShapeDrawing` substitutes
by sampler key while binding, so the G-buffer, z-only and forward variants all pick it up even
though the same sampler sits on a different unit in each one's compiled program.

This is the runtime half of what `nn::g3d2::MaterialAnimObj::ApplyTo` (Ghidra 0x7100080894)
does - it writes the texture the pattern curve selected into the material object's sampler slot.
The selection itself is `Evaluate`, a step curve; see
`ShaderLibrary.CompileTool.ExportTexturePatternAnim` for the full derivation.
