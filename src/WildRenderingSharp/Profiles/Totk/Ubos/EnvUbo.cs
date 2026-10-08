using System.Numerics;
using WildRenderingSharp.Shaders.Common;

namespace WildRenderingSharp.Profiles.Totk.Ubos;

/// <summary>
/// TotK <c>gsys_environment</c> ("Env", decompiled as <c>fp_c9</c>), binding 6, 1328 bytes.
///
/// Bytes 0..415 are CONFIRMED three independent ways (BFSHA-adjacent declaration records, an
/// exact field-for-field match against Splatoon 3's labelled <c>gsys_environment</c> source, and
/// every read of it below byte 416 across all 23 deferred passes landing on a declared field) -
/// see <c>TestBench/Shaders/Decompiled/gsys_environment_layout.glsl</c>. Bytes 416..1327 are a
/// TotK-specific extension with no labelled source to cross-reference; only the handful of slots
/// a real deferred pass (<c>chara_metal</c>, program 6) is known to read are given a meaning here
/// (<see cref="Slots.AmbientHeightAttenuation"/>, <see cref="Slots.ShadowDepthBias"/>,
/// <see cref="Slots.ShadowMapDimensions"/>, <see cref="Slots.VolumeMaskTint"/>, and
/// <see cref="PowExponentSlots"/>) - the rest of the extension stays zeroed rather than guessed.
/// </summary>
public sealed class EnvUbo : IUboBlock
{
    public const int ByteSize = 1328;

    public static class Slots
    {
        public const int AmbientColor = 0;               // +0    cLightAmbColor
        public const int HemiSkyColor = 1;                // +16   cLightHemiSkyColor
        public const int HemiGroundColor = 2;              // +32   cLightHemiGroundColor
        public const int HemiDir = 3;                      // +48   cLightHemiDir.xyz, +60 cLightPadd0
        public const int LightDir0 = 4;                    // +64   cLightDir0.xyz, +76 cLightIntensity0
        public const int LightColor0 = 5;                  // +80
        public const int LightSpecColor0 = 6;               // +96
        public const int LightDir1 = 7;                     // +112  cLightDir1.xyz, +124 cLightPadd1
        public const int LightColor1 = 8;                   // +128
        public const int LightSpecColor1 = 9;                // +144
        public const int FogColor = 10;                      // +160  fog group 0 (view-space)
        public const int FogDir = 11;                         // +176  .xyz, +188 cFogStart
        public const int FogStartEndInvDamp = 12;              // +192  .x cFogStartEndInv, .y cFogDamp
        public const int WorldFogColor = 13;                   // +208  fog group 1 (world-space)
        public const int WorldFogDir = 14;                      // +224  .xyz, +236 cWorldFogStart
        public const int WorldFogStartEndInvDamp = 15;           // +240
        public const int WorldFogMaskColor = 16;                 // +256  fog group 2 (world-space mask)   <- read by deferred passes
        public const int WorldFogMaskDir = 17;                   // +272  .xyz, +284 cWorldFogMaskStart    <- read
        public const int WorldFogMaskStartEndInvDamp = 18;        // +288                                   <- read
        public const int FogFxColor = 19;                        // +304  fog group 3 (view-space fx)      <- read
        public const int FogFxDir = 20;                          // +320  .xyz, +332 cFogFxStart           <- read
        public const int FogFxStartEndInvDamp = 21;               // +336                                   <- read
        public const int HemiDirWorld = 22;                       // +352
        public const int LightDir0World = 23;                     // +368                                   <- read
        public const int LightDir1World = 24;                     // +384
        public const int Unknown25 = 25;                          // +400  one further declaration, type not recovered

        // ---- TotK extension (bytes 416..1327), only the fields with a confirmed read site ----

        /// <summary>
        /// VERIFIED by reading chara_metal (prog 6) directly: a WORLD-SPACE-height-based
        /// attenuation of the half-lambert ambient term (.x/.y = clamp curve params, .z = 1
        /// disables it for any height - the neutral used here). NOT screen-space AO, despite an
        /// earlier research pass guessing that.
        /// </summary>
        public const int AmbientHeightAttenuation = 47;
        /// <summary>Only read by the (unimplemented) preshading_* passes: .y = shadow depth bias.</summary>
        public const int ShadowDepthBias = 50;
        /// <summary>Only read by preshading_*: .xy = shadow-map tile dimensions, packed as ints via floatBitsToInt.</summary>
        public const int ShadowMapDimensions = 52;
        /// <summary>
        /// VERIFIED: prog 6's alpha output multiplies by <c>1 - exp2(log2(0) * this.z) * this.w</c>.
        /// <c>.z</c> must be nonzero (see <see cref="PowExponentSlots"/> - log2(0) = -inf, and
        /// exp2(-inf * 0) is NaN) for the term to collapse to 0 and leave alpha untouched.
        /// </summary>
        public const int Unknown70 = 70;
        /// <summary>
        /// VERIFIED: volume-mask tint on the ambient - <c>cTex_VolumeMask.z * this.w</c> lerps
        /// ambient.rgb toward <c>ambient.rgb * this.xyz</c>. <c>.w = 0</c> (no VolumeMask texture)
        /// is the correct neutral, matching an all-zero <c>cTex_VolumeMask</c>.
        /// </summary>
        public const int VolumeMaskTint = 81;
    }

    /// <summary>
    /// Every (slot, component) the decompiled shaders use as a <c>pow(x, k)</c> exponent, found by
    /// scanning for <c>exp2(log2(expr) * slot.component)</c> - the compiler's rendering of
    /// <c>pow</c>. Left at the default zero, <c>k = 0</c> evaluates <c>exp2(-inf * 0) = NaN</c>
    /// whenever the base is 0, instead of the mathematically correct <c>pow(x, 0) = 1</c>; TotK's
    /// forward shaders hit this on every frame if these are left zeroed. (18,1)/(21,1) sit in the
    /// decoded fog region (they are <c>cWorldFogMaskDamp</c>/<c>cFogFxDamp</c> - being read as a
    /// pow exponent elsewhere is a real dual use, not a naming mistake); the rest are in the
    /// undecoded extension and are pure placeholders.
    /// </summary>
    public static readonly (int Slot, int Component)[] PowExponentSlots =
    {
        (18, 1), (21, 1),
        (27, 0), (27, 1), (29, 0), (29, 2),
        (57, 2), (57, 3), (70, 2), (77, 2),
    };

    readonly Std140Block _block = new(ByteSize);

    public string Name => "Env";
    public int BindingIndex => 6;
    public int SizeBytes => ByteSize;

    /// <summary>
    /// Packs the fields a real deferred resolve pass (chara_metal/chara_nonmetal) reads. Mirrors
    /// <c>build_env</c> exactly, including its defaults for the (currently unimplemented)
    /// preshading-only fields. <paramref name="hemiSkyColor"/>/<paramref name="hemiGroundColor"/>
    /// are already resolved by the caller (palette lookup + intensity/ambient-scale multiply
    /// lives in <c>WildRenderingSharp</c>, not here, to keep this class palette-agnostic).
    /// </summary>
    public static EnvUbo BuildFromLighting(
        Vector3 sunDirView, Vector3 sunDirWorld, Vector3 sunColor,
        Vector3 hemiSkyColor, Vector3 hemiGroundColor,
        Vector3 volumeMaskColor, float volumeMaskIntensity,
        int shadowMapSize)
    {
        var env = new EnvUbo();
        var b = env._block;

        b.SetSlot(Slots.AmbientColor, 0.10f, 0.11f, 0.13f, 1f);
        b.SetVec3(Slots.HemiSkyColor, hemiSkyColor, 1f);
        b.SetVec3(Slots.HemiGroundColor, hemiGroundColor, 1f);

        // cLightDir0 is the direction light TRAVELS (from sun toward scene); every deferred
        // resolve pass evaluates direct lighting as 0 - dot(N, cLightDir0), so it must be the
        // negation of sunDirView (which points TOWARD the sun).
        b.SetVec3(Slots.LightDir0, -sunDirView, 1f); // .w = cLightIntensity0, folded into sunColor upstream
        b.SetVec3(Slots.LightColor0, sunColor, 1f);
        b.SetVec3(Slots.LightSpecColor0, sunColor, 1f);

        // The WORLD-space copies of the same directions. Only cLightDir0World is actually read
        // (100 shaders), and its one observed use pins the meaning down: TotK's sky-island shadow
        // samples cTex_SkyIslandShadow at
        //     vec2(worldX - cLightDir0World.x * h, worldZ - cLightDir0World.z * h)
        // i.e. it walks the shading point BACK along the light's travel direction by a height h
        // (SceneMat[28].w) to find where it sits under the island - which only works if this is the
        // direction light travels, in world space, matching cLightDir0's view-space convention.
        // Left at zero the walk-back distance was always zero, so every point sampled the shadow
        // map directly overhead. The other two have no readers in the extracted set; they are
        // written because they are cheap and being right costs nothing, not because anything needs
        // them yet.
        b.SetVec3(Slots.HemiDirWorld, Vector3.UnitY, 0f);
        b.SetVec3(Slots.LightDir0World, -sunDirWorld, 0f);
        b.SetSlot(Slots.LightDir1World, 0f, -1f, 0f, 0f);

        b.SetSlot(Slots.LightDir1, 0f, -1f, 0f, 0f);
        b.SetSlot(Slots.LightColor1, 0f, 0f, 0f, 1f);
        b.SetSlot(Slots.LightSpecColor1, 0f, 0f, 0f, 1f);

        // Four fog groups, all disabled (density 0 in .a; StartEndInv/Damp left zero so any
        // depth-based interpolation collapses to nothing) - for their own, properly alpha-gated
        // fog-blend usage. BUT: Enemy_MiasmaTentacle's Mt_Skin forward program
        // (material_prog10336_extracted.frag, confirmed by hand-tracing + the live numeric probe
        // this session) reads WorldFogMaskColor (slot 16) and FogFxColor (slot 19) a SECOND way -
        // as a bare, UNGATED `temp_320 * fp_c9.data[19].x`-style multiplier feeding directly into
        // this program's own final highlight colour, with .w (the density this whole "disabled"
        // scheme relies on) never appearing in that expression at all. The previous bluish-gray
        // placeholder (0.55, 0.62, 0.72) - chosen as an inert-looking generic sky tint for the
        // fog-blend usage, long before this second usage was known about - LEAKS straight through
        // this ungated read and skews every material relying on it toward cyan/blue, including
        // Mt_Skin's real, correctly-authored dark red (p_const_color2/3) - confirmed: this is why
        // its forward-pass highlight rendered yellow-green/cyan instead of red even after the
        // additive-blend and exposure fixes above. Real per-region romfs data
        // (`WorldMgr/ResEnvPalette/MainField_Underground.game__wm__ResEnvPalette.bgyml`, the
        // Depths' own palette - see docs/env_slot47_research_request.md for how this was found)
        // has an authored `FogColor` of (0.001, 0.005, 0.001) - i.e. genuinely near-black, not
        // grey-blue - so zero is a faithful stand-in, not a fresh guess, and remains just as inert
        // for the fog-blend usage (already zeroed twice over via .a and Start/End/Damp).
        Span<int> fogColorSlots = [Slots.FogColor, Slots.WorldFogColor, Slots.WorldFogMaskColor, Slots.FogFxColor];
        foreach (int slot in fogColorSlots)
            b.SetSlot(slot, 0f, 0f, 0f, 0f);

        // Env[47].z = 1 makes the height-based ambient attenuation a no-op for any height - VERIFIED
        // for chara_metal (a deferred pass), by reading its own shader math directly.
        //
        // Env[47].x/.y: RESOLVED, CLOSED - confirmed inert, not worth further Ghidra archaeology.
        // A real forward (gsys_assign_material) program on Enemy_MiasmaTentacle's Mt_Skin does read
        // this same slot's .x/.y (`temp_346 = clamp(fma(temp_11, Env[47].x, Env[47].y), 0, 1)`,
        // material_prog10336_extracted.frag - re-verified against a freshly re-prepared copy of this
        // exact program, confirmed via MC_DEBUG_OPTIONSEARCH to be an exact, non-fallback match) -
        // but its ONE AND ONLY consumer is `temp_348 = fma(fma(temp_346, Env[47].z, -temp_346),
        // fma(temp_29, 0.5, 0.5), fma(temp_29, 0.5, 0.5))`, the SAME "z-protected" shape every other
        // confirmed usage of this slot uses. At Env[47].z = 1 (independently confirmed correct via
        // chara_metal, above), this algebraically collapses to `fma(temp_29, 0.5, 0.5)` - completely
        // INDEPENDENT of temp_346, and therefore of .x/.y, for every possible input. An earlier pass
        // this session read a DIFFERENT-numbered temp (`temp_323`, from an older `--prepare` run of
        // the same program - temp_N numbering is not stable across re-preparations, see CLAUDE.md)
        // and concluded this usage lacked the .z protection every other one has; that conclusion was
        // wrong, not a change in the real shader - re-reading the current file shows the protection
        // was there all along. This also explains why the earlier `.y=1` experiment (see git history)
        // produced no visible change: with .z=1, .x/.y cannot affect this program's output at all,
        // by construction, regardless of what they're set to. (0,0,1,0) is kept as-is - it is
        // provably as correct as any other value for .x/.y specifically; only .z matters, and .z is
        // already right. The real, still-open cause of Mt_Skin's wrong colour is downstream of this
        // slot entirely - see tasks_set1.md's temp_339/temp_366-368-equivalent thread (also renumbered
        // in the current file; needs re-locating there, not here).
        b.SetSlot(Slots.AmbientHeightAttenuation, 0f, 0f, 1f, 0f);
        // Env[70].zw: .z nonzero collapses the alpha term to 0 rather than NaN; .w unused here.
        b.SetComponent(Slots.Unknown70, 2, 1f);
        b.SetComponent(Slots.Unknown70, 3, 0f);
        // Env[81]: volume-mask tint, driven by the palette's VolumeMaskColor/Intensity (0 = inert).
        b.SetVec3(Slots.VolumeMaskTint, volumeMaskColor, volumeMaskIntensity);

        b.SetComponent(Slots.ShadowDepthBias, 1, 0.0005f);
        b.SetComponent(Slots.ShadowMapDimensions, 0, BitConverter.Int32BitsToSingle(shadowMapSize));
        b.SetComponent(Slots.ShadowMapDimensions, 1, BitConverter.Int32BitsToSingle(shadowMapSize));

        // Any pow-exponent slot left at the default 0 becomes NaN the instant a forward shader
        // evaluates pow(0, 0) through the compiler's exp2(log2(x)*k) idiom; 1.0 (pow(x,1) = x) is
        // NaN-free and, for the two decoded fog-damp dual-use slots, also the correct "no curve".
        foreach (var (slot, comp) in PowExponentSlots)
        {
            if (b.GetComponent(slot, comp) == 0f)
                b.SetComponent(slot, comp, 1f);
        }

        return env;
    }

    public void WriteTo(Span<byte> destination) => _block.WriteTo(destination);
    public byte[] ToByteArray() => _block.ToByteArray();
}
