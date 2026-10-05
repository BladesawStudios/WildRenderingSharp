# Research request: `gsys_environment` slot 47 (`.x`/`.y`) real value

**Purpose of this document**: a focused hand-off for an external, fast research pass (the user is
routing this to another AI) on ONE specific, narrow question that's proven too large for this
session's own Ghidra tool (function bodies over ~50KB time out or exceed this session's per-call
token budget). **Treat whatever comes back as unverified until it's checked against the actual
Ghidra output and this repo's code** - same policy as `docs/forward_cyan_bug_research_request.md`
earlier this session, which worked well.

## The question, precisely

TotK's `gsys_environment` UBO (binding 6, decompiled as `fp_c9`, 1328 bytes total: bytes 0-415 are
a confirmed base region, bytes 416-1327 are a mostly-unconfirmed "extension" region - see
`docs/gsys_environment.md`) has a field at **byte offset 752-767** (16-byte slot 47,
`fp_c9.data[47]`) that Marrow's own code (`EnvUbo.cs`) currently sets to `(0, 0, 1, 0)`.

- `.z = 1` is CONFIRMED correct (verified independently by reading a real deferred shader,
  `chara_metal`/program 6, directly - not from Ghidra).
- `.x` and `.y` are NOT confirmed. This request is about finding their real value.

## Why this matters (context, not required reading to answer the question)

Marrow re-renders real TotK models using the game's own decompiled shaders. A forward-pass shader
bug (`Enemy_MiasmaTentacle`'s `Mt_Skin` material rendering as flat cyan instead of a dark
red/black "Gloom Hand" look) was traced, this session, all the way down to: a real, shared "chara
forward" shader formula multiplies an entire real-lighting contribution by
`temp_323 = clamp(fma(someLocalTerm, fp_c9.data[47].x, fp_c9.data[47].y), 0, 1)`. At the current
`(0, 0)`, this clamps to exactly 0, zeroing that whole lighting term. This exact formula was
confirmed byte-identical in a SECOND, unrelated creature's forward shader too (`Enemy_Bokoblin`),
so it's shared boilerplate, not a one-off.

## What's already been ruled out / confirmed this session (don't re-derive these)

- The shape's compiled forward program is an EXACT MATCH resolution (no closest-program fallback,
  no static-option normalisation) - confirmed via `MC_DEBUG_OPTIONSEARCH=1` - so this is genuinely
  the real, Nintendo-shipped compiled shader, not a wrong-variant bug.
- Every OTHER confirmed real usage of this same slot 47 (the deferred `chara_metal`/`chara_skin`
  passes, AND a second usage within the very same forward shader) protects itself via `.z`: the
  pattern `fma(fma(X, Env47.z, -X), Y, Y)` at `Env47.z = 1` collapses to exactly `Y`, making the
  result INDEPENDENT of whatever `X` (and therefore `.x`/`.y`) is. Only ONE specific usage site (in
  the "chara forward" boilerplate, feeding a term called `temp_323`/`temp_145` depending on the
  file) reads `.x`/`.y` directly as a bare multiplicative gate with NO such protection.
- Tried `.x=0, .y=1` (reasoning: mirrors the "saturate via the additive/bias term" shape `.z`
  already uses) as a live, in-app experiment. **User confirmed this did NOT visibly fix the
  colour.** This doesn't necessarily mean `(0,1)` is wrong for THIS slot - there's a SEPARATE,
  independent near-zero factor in the same lighting formula (see below) that would mask any
  improvement here anyway. Don't treat the failed experiment as strong evidence either way.
- The `gsys_environment` extension region (bytes 416-1327) has NO compiled default in the shader
  archive (confirmed via a new `ShaderLibrary.CompileTool --dump-uniform-blocks` flag this
  session - it prints each field's real `DefaultBuffer` value when one exists; this whole region
  has none). So unlike `gsys_scene_material` (a DIFFERENT UBO, which turned out to be an ordinary
  material's authored parameters on a dedicated romfs model, `SystemModel.SceneMaterial.bfres.mc` -
  a full success story this session, see `tasks_set1.md`), `gsys_environment`'s extension really is
  live engine-computed scene state, written by C++ at a raw byte offset with no name-based lookup
  (confirmed: no `p_...`-style string exists for it, unlike the confirmed "Dynamic" scene-material
  fields which ARE looked up by name at runtime - see `setDynamicShadowParams` in
  `docs/gsys_environment.md`/`tasks_set1.md` for that contrast).

## The lead: `game::wm::WorldEnvMgr::calc`

Traced (this session, via Ghidra) a plausible real ownership chain:

1. `game::wm::WorldMgrBase::calcEnv` (`0x7100b39c3c`, TotK executable) builds a large local
   `EnvPaletteInfo`-shaped structure on the stack (sun-angle trig via `cosf`/`sinf`, several
   `0x3f800000` = `1.0f` float constants, fog-colour-shaped fields) by querying a chain of
   `game::wm::EnvPaletteInfo`-related accessors.
2. It then calls `game::wm::WorldEnvMgr::calc(param_1[3], &thatStruct, param_3)`.
3. `game::wm::WorldEnvMgr::calc` is at `0x7100b3aec8` - **this is the function that most plausibly
   owns writing the real `gsys_environment` extension bytes**, but it is genuinely huge (function
   body `0x7100b3aec8`-`0x7100b3e0bb`, ~12.8 KB of ARM64 machine code) - too large for this
   session's Ghidra MCP tool to decompile within its timeout (repeated `Read timed out` after 5s),
   and too large to disassemble-and-read directly either (231KB+ of raw disassembly text).

### Sub-calls already extracted from `WorldEnvMgr::calc`'s disassembly (a head start)

Pulled via `bl` (branch-with-link) instructions in the raw disassembly - these are every function
`WorldEnvMgr::calc` itself calls, as raw addresses (Ghidra's disassembly view didn't resolve them
to symbol names the way its decompiler view would - `decompile_function_by_address` on THIS
address times out, but the same call on any ONE of these smaller sub-addresses individually should
work fine and reveal its real name/purpose):

```
0x7100698b30  0x7100a035f8  0x7100b3a90c  0x7100b3e0bc  0x7100b3e110  0x7100b3e268
0x7100b3e494  0x7100b3ec30  0x7100b3f040  0x7100b3fe84  0x7100b420e4  0x7100b429b4
0x7100b42b18  0x7100b42cf0  0x7100b42da8  0x7100b4b944  0x7100b4f2d4  0x7100b5041c
0x7100b50820  0x7100d925f4  0x7100d979f4  0x7100ded784  0x710101c2a8  0x7101083d1c
0x71012308c8  0x71014fdcbc  0x7101624d48  0x7101b55c88  0x7102b17290  0x7102b172b0
0x7102b172e0  0x7102b17660  0x7102b17670  0x7102b17ad0
```

The ones clustered in `0x7100b3a9xx`-`0x7100b50xxx` are most likely private helper methods of
`WorldEnvMgr` itself (called internally, breaking up this one giant function) - probably the most
fruitful to check first, in roughly call order. The `0x7102b17xxx` cluster (5 calls, all close
together, likely near the end of the function) is also worth a look as a distinct group.

## What a good answer looks like

Ideally: a confirmed byte offset write, within `WorldEnvMgr::calc` or one of its sub-calls, landing
at the `gsys_environment` extension buffer's offset corresponding to slot 47 (byte 752 within the
1328-byte block overall; if the extension is handled as its own separate 912-byte buffer
internally - it's declared as one opaque `agl::UniformBlock::declare_` blob starting at UBO byte
416, see `docs/gsys_environment.md` - the equivalent OFFSET WITHIN JUST THAT SUB-BUFFER would be
`752 - 416 = 336`), for `.x` (offset 752/336) and `.y` (offset 756/340) specifically, with the real
float value(s) written there.

Failing a fully confirmed write site: ANY partial evidence is still useful - e.g. finding a
plausible "world height" or "ambient occlusion by depth" concept being computed anywhere in this
call chain (given the field's own working name, `AmbientHeightAttenuation`, inferred from a
DIFFERENT program's usage of `.z`), even without pinning the exact byte offset, would help decide
whether `(0, 1)` (already tried, didn't visibly help - but see the masking caveat above), some
other constant, or a formula depending on other real inputs is the right shape.

## A second, independent, possibly-easier thread (pure shader math, no Ghidra needed)

The SAME lighting formula this `temp_323` gates also depends on a SECOND, INDEPENDENT term,
`temp_339`, which the user confirmed (via Marrow's own live numeric probe tool) is currently only
`~0.01` - suspiciously small, but NOT a hard zero the way `temp_323` is, so it isn't obviously
caused by one missing UBO field. Its real formula (from the same decompiled shader,
`material_prog10336_extracted.frag`, `%AppData%\Marrow\cache\_shaders\` on the user's machine -
not attached here, but re-extractable via `ShaderLibrary.CompileTool --prepare`) is:

```glsl
temp_338 = clamp(clamp(temp_334 + 0.08, 0.0, 1.0) * clamp(fma(clamp(fma(temp_308, temp_292, -0.05) * 20.0, 0.0, 1.0), 0.12, clamp(fma(temp_308, temp_292, -0.01) * 50.0, 0.0, 1.0) * 0.05), 0.0, 1.0), 0.0, 1.0);
temp_317 = max(max(temp_162, temp_163), temp_164);
temp_339 = clamp(fma(temp_338, fma(temp_329 * temp_325, 3.0, temp_325), temp_317), 0.0, 1.0);
```

(`temp_329` also depends on `temp_323`, i.e. the SAME slot-47 issue above, but `temp_338`/`temp_317`
trace to other, separate local terms not yet fully traced this session - `temp_334`/`temp_308`/
`temp_292`/`temp_162..164` etc. This could be a purely-textual shader-tracing task, no exe access
needed, if useful groundwork to do in parallel.)

## Explicit caution

As with the earlier research doc this session: **verify anything found here against the real
Ghidra decompile output and the real shader text before writing it into Marrow's code.** This
session already tried one plausible-but-wrong guess (`(0, 1)`) and confirmed empirically, in the
real running app, that it didn't fix the reported bug - a reminder that "looks like a reasonable
default" is not the same as "confirmed correct," and that this specific bug has more than one
contributing factor stacked together.
