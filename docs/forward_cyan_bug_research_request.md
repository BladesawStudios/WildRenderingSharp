# Research request: forward-pass cyan bug (for Gemini / exe archaeology)

**Purpose of this document**: hand-off to an external, fast/cheap research pass (Google Gemini)
over the *Tears of the Kingdom* executable, so it can propose likely answers quickly. **Treat
whatever comes back as unverified and unsafe to act on** until a human/Claude cross-checks it
against the actual decompiled code in Ghidra and the actual shader/UBO source in this repo. Gemini
may get addresses or exact values wrong — that's fine, it only needs to point at the right
functions/fields fast.

## Project context (one paragraph)

Marrow is a live GL viewer that re-renders real TotK models using the game's own decompiled
shaders (BFSHA → GLSL, done offline by a separate tool; Marrow only consumes the already-decompiled
`.vert`/`.frag` text and pre-built UBO byte blocks — it does not re-derive shader logic itself).
Its G-buffer + deferred-resolve pipeline is solid and visually correct. It also has a `ForwardPass`
that runs each shape's `gsys_assign_material` program — this is a full second, independently-lit
shading pass the real engine uses for effects layered on top of the base material (status effects:
miasma/gloom corruption, ice, vanishing, mottled/blueprint, etc.), not just alpha-blended objects.
Marrow only recently started running this pass for **opaque** shapes too (previously it only ran
for alpha-blended shapes, which was wrong — plenty of opaque enemies have a real, populated forward
program). That change is currently gated behind an opt-in checkbox
(`LightingContext.ForceForwardOnOpaqueShapes`) specifically because of the bug below.

## The bug

With `ForceForwardOnOpaqueShapes` on, `Enemy_MiasmaTentacle` (a gloom/miasma-corrupted
enemy — a clawed hand model) and a similar Ganondorf/Phantom-Ganon-like corrupted model render as a
flat, bright, uniformly **cyan** silhouette instead of their proper dark corrupted-flesh look with
an orange/red glowing eye motif. Screenshot on file with the user; not reproducible by me directly
(I don't self-verify visually — see `CLAUDE.md` — the user tests every change in the real app).

### Already ruled out this session

- **Not exposure/tonemap blowout.** User explicitly tested and confirmed the cyan persists
  regardless of exposure compensation theories.
- **Not a wrong GPU blend state.** Traced `gsys::ModelRenderState::update` in Ghidra: it's a
  generic RenderInfo-string → GPU-blend-state resolver used identically for *every* pass name
  (gbuffer, material, zonly, ...), not something forward-pass-specific. Marrow's own
  `RenderState.cs` already mirrors this exact generic logic, so a forward-specific blend bug isn't
  possible here — the resolution path is shared code that already works for every other pass.
- **Not a Y-flip issue.** A real, separate mirroring bug in `ForwardPass.Run` (wrong Context UBO
  bound, causing geometry to draw unflipped into a flipped target then get double-flipped) was
  found and fixed independently this session. Unrelated to the color problem.
- **Not a missing "system" texture.** `cTex_Proc3DNoise` (`3DWorleyPerlinNoise_Fi.bntx.zs`, a real
  64×64×64 BC4 volume, TotK's only confirmed non-`.txtg` "system" texture) was extracted, BC4-decoded
  (verified correct by rendering a slice to PNG and visually confirming real Worley/Perlin noise),
  and wired into `ForwardPass`. Did not change the cyan result when the user re-tested.

## The strongest lead found so far — please prioritize this

Both `Enemy_MiasmaTentacle`'s and `Enemy_Bokoblin`'s forward (`gsys_assign_material`) fragment
programs share **near-identical boilerplate** late in the shader (this looks like shared
compiler-inlined code common to every "chara" forward program, not something specific to either
enemy). Files (already decompiled, plain GLSL, in `%AppData%\Marrow\cache\_shaders\`):

- `material_prog10336_extracted.frag` — Enemy_MiasmaTentacle, `Mt_Skin` shape, forward program.
- `material_prog3248_extracted.frag` — Enemy_Bokoblin, `Mt_Skin` shape, forward program.

Two UBOs are involved: `fp_c9` = `gsys_environment` ("Env", binding 6, see
`src/WildRenderingSharp/Shaders/Profiles/Totk/Ubos/EnvUbo.cs` and the pre-existing `docs/gsys_environment.md`), and
`fp_c13` = `gsys_scene_material` ("SceneMat", binding 10, see
`src/WildRenderingSharp/Shaders/Profiles/Totk/Ubos/SceneMatUbo.cs`). Unlike Env, **every SceneMat field name and
byte offset is already known for certain** — it comes straight from the shading model's own BFSHA
reflection metadata, not guesswork (see the `Fields` class in `SceneMatUbo.cs`). Marrow's
`SceneMatUbo.BuildFromLighting` only ever *writes* about 10 of its 88 named fields; everything else
defaults to zero.

### Finding 1: `DynamicMonochromeSaturationForChara` (SceneMat byte 208, `fp_c13.data[13].x`) is always 0

Identical structure in both shaders (Bokoblin shown, MiasmaTentacle is the same one line later):

```glsl
if (fp_c13.data[13].x < 1.0)
{
    temp_355 = max(temp_346, max(temp_347, temp_348));   // max(R, G, B)
    temp_351 = floatBitsToInt(fma(temp_346 - temp_355, fp_c13.data[13].x, temp_355));  // R' = lerp(maxRGB, R, x)
    temp_352 = floatBitsToInt(fma(temp_347 - temp_355, fp_c13.data[13].x, temp_355));  // G'
    temp_353 = floatBitsToInt(fma(temp_348 - temp_355, fp_c13.data[13].x, temp_355));  // B'
}
```

This is a "desaturate each channel toward max(R,G,B)" lerp, controlled by
`DynamicMonochromeSaturationForChara` acting as the lerp factor (0 = fully collapsed to
max-channel/monochrome, 1 = untouched, and the `if (x < 1.0)` guard means it's skipped entirely
only at exactly 1.0). Marrow's `SceneMatUbo.BuildFromLighting` never writes this field, so it is
**always 0 → both models always get fully collapsed to a monochrome value on every frame**,
regardless of whether they should currently be under any status effect.

**Question for the exe**: what real system populates `gsys_scene_material` per frame (the
counterpart to whatever populates `gsys_environment`, which `docs/gsys_environment.md` already
partially traces to `gsys::ModelRenderContext::initialize` and friends)? Specifically, is
`DynamicMonochromeSaturationForChara` normally driven to `1.0` (identity/off) for a healthy enemy
and only pulled toward `0.0` by some specific gloom/corruption status system? Or is `0` actually
correct for a corrupted enemy and the real bug is elsewhere? The field's own name — a per-"chara"
(character) desaturation control read specifically inside the "gloom corruption" model's forward
program — strongly suggests it's a deliberate gameplay-driven effect, which would mean Marrow is
simply missing whatever normally drives it back to 1.0 as a baseline default.

### Finding 2: undeclared `gsys_environment` extension slots mixed with a placeholder fog color

A few lines earlier, both shaders compute the final per-channel color with a chain like this
(again nearly identical in both files, R-channel version shown):

```glsl
... = fma(temp_297, fp_c9.data[30].x,
      fma(temp_295 * temp_260.x, temp_302,
      fma(temp_307, fp_c9.data[68].x,
      fma(temp_311, fp_c9.data[16].x, temp_309 * fp_c9.data[19].x))));
```

- `fp_c9.data[16]` = `WorldFogMaskColor`, `fp_c9.data[19]` = `FogFxColor` — both **confirmed,
  declared** Env fields (see `EnvUbo.Slots`). Marrow deliberately sets both to `(0.55, 0.62, 0.72,
  0)`, a bluish "sky fog" placeholder color, with alpha (density) 0 meant to disable the fog
  itself — but the raw `.rgb` is still read here regardless of the `.a` disable.
- `fp_c9.data[30]` and `fp_c9.data[68]` sit in the **still-undeciphered TotK extension region**
  (bytes 416–1327) that `docs/gsys_environment.md` explicitly documents as mostly unconfirmed —
  Marrow leaves them at hard zero because no real deferred pass Marrow has traced so far reads
  them, only these forward programs do.

So this formula blends a real, deliberately-blue placeholder color together with two zeroed unknown
slots, directly into final output color, on every pixel. If the real values of slots 30/68 are
something very different (say, a warm corruption-glow tint, or simply a near-black "off" value),
mixing our placeholder blue-gray fog color in their place is a very plausible direct cause of the
reported cyan skew (blue+green channels systematically pulled up relative to red).

**Question for the exe**: what does `gsys_environment` (Env/`fp_c9`) actually contain at byte
offset 480 (16-byte-slot index 30) and byte offset 1088 (slot 68) during a normal scene? Per
`docs/gsys_environment.md` section 5 ("to finish the job properly"), the extension region's real
field names/values were never fully recovered because several other `startDeclare` callers were
never walked:

- `FUN_7100c97b18`
- `agl::pfx::Sky::Sky::initialize`
- `agl::fx::Cloud::initialize`
- `gsys::RenderBufferContext::initialize`
- `FUN_7100e21940`
- `FUN_7100e29aa8`
- `FUN_7100e2c7a0`

If Gemini can walk any of these and find writes that land at Env byte offsets 480 or 1088
specifically (or anywhere in the 416–1327 range generally — every hit helps, not just these two),
that directly extends the existing recovery table in `docs/gsys_environment.md`.

## Secondary thread (lower priority, only if time permits)

Before landing on the above, I traced `gsys::ModelShader::initialize` and confirmed it builds a
per-shading-model bitmask (stored at struct offset `0xC0`) of which of the 23 valid
`gsys_assign_*` pass names that shading model declares a program for. I was **not** able to find
where this bitmask is later *read* — i.e., the actual per-object runtime decision of whether to
invoke `gsys_assign_material` (the forward pass) for a given object this frame. If that dispatch
logic turns out to be conditional on something other than "shading model declares the pass" (e.g.
gated on an active status-effect flag on the object), that could mean Marrow's opt-in
"run forward for all opaque shapes unconditionally" premise is itself wrong, and the fix is to only
run it when some equivalent status-effect condition is true — which would sidestep the color
question above entirely for un-corrupted enemies, though MiasmaTentacle/Phantom-Ganon are always
meant to look corrupted, so it wouldn't explain away Findings 1/2 for those two specifically.
If you can find this dispatch site (functions calling into whatever resolves
`gsys::ModelShader::initialize`'s bitmask, or any function that reads a `+0xC0` field on what looks
like a per-shading-model or per-material object), name it and quote the decompiled logic.

## What to hand back

For each of the two Findings above and the secondary thread, please give:
1. The Ghidra function name/address you looked at.
2. A short decompiled snippet or paraphrase of what it writes/reads.
3. Your best guess at the real value(s), clearly labeled as a guess if you're not certain.
4. Anything that contradicts the hypotheses above — that's just as useful as confirmation.

Cross-reference files already in this repo for context, in case useful: `CLAUDE.md` (project
overview), `docs/gsys_environment.md` (existing Env UBO recovery, its own section 5 lists the exact
unexplored callers above), `src/WildRenderingSharp/Shaders/Profiles/Totk/Ubos/EnvUbo.cs` and
`SceneMatUbo.cs` (current Marrow-side field tables and what's actually populated today).
