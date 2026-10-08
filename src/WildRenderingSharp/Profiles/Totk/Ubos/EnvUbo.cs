using System.Numerics;
using WildRenderingSharp.Graphics;

namespace WildRenderingSharp.Profiles.Totk.Ubos;

/// <summary>TotK <c>gsys_environment</c> ("Env", decompiled as <c>fp_c9</c>), binding 6, 1328 bytes.</summary>
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

        // TotK extension (bytes 416..1327): only fields with a confirmed read site.

        public const int AmbientHeightAttenuation = 47;
        public const int ShadowDepthBias = 50;
        public const int ShadowMapDimensions = 52;
        public const int Unknown70 = 70;
        public const int VolumeMaskTint = 81;
    }

    public static readonly (int Slot, int Component)[] PowExponentSlots =
    {
        (18, 1), (21, 1),
        (27, 0), (27, 1), (29, 0), (29, 2),
        (57, 2), (57, 3), (70, 2), (77, 2),
    };

    readonly Std140Block _block = new(ByteSize);

    public string Name => "Env";
    public int BindingIndex => (int)TotkBindings.Environment;

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

        // cLightDir0 is the direction light travels, and every resolve pass evaluates direct lighting as 0 - dot(N, cLightDir0),
        // so it is the negation of sunDirView, which points toward the sun.
        b.SetVec3(Slots.LightDir0, -sunDirView, 1f); // .w = cLightIntensity0, folded into sunColor upstream
        b.SetVec3(Slots.LightColor0, sunColor, 1f);
        b.SetVec3(Slots.LightSpecColor0, sunColor, 1f);

        // World-space copies of the same directions. Only cLightDir0World is read (by 100 shaders): the sky-island shadow
        // samples cTex_SkyIslandShadow at vec2(worldX - cLightDir0World.x * h, worldZ - cLightDir0World.z * h), walking the
        // shading point back along the light's world-space travel direction by a height h (SceneMat[28].w). At zero every
        // point sampled directly overhead. The other two have no readers and are written because it is cheap.
        b.SetVec3(Slots.HemiDirWorld, Vector3.UnitY, 0f);
        b.SetVec3(Slots.LightDir0World, -sunDirWorld, 0f);
        b.SetSlot(Slots.LightDir1World, 0f, -1f, 0f, 0f);

        b.SetSlot(Slots.LightDir1, 0f, -1f, 0f, 0f);
        b.SetSlot(Slots.LightColor1, 0f, 0f, 0f, 1f);
        b.SetSlot(Slots.LightSpecColor1, 0f, 0f, 0f, 1f);

        // Four fog groups, all disabled (density 0 in .a, StartEndInv and Damp zero so depth interpolation collapses). But
        // Enemy_MiasmaTentacle's Mt_Skin forward program (material_prog10336) also reads WorldFogMaskColor (slot 16) and
        // FogFxColor (slot 19) as a bare multiplier into its final highlight colour, ungated by .w, so a non-zero placeholder
        // leaks through and skews it toward cyan. The Depths' own palette (MainField_Underground) authors FogColor
        // (0.001, 0.005, 0.001), so zero is a faithful stand-in and stays inert for the gated fog-blend usage
        // (see docs/env_slot47_research_request.md).
        Span<int> fogColorSlots = [Slots.FogColor, Slots.WorldFogColor, Slots.WorldFogMaskColor, Slots.FogFxColor];
        foreach (int slot in fogColorSlots)
            b.SetSlot(slot, 0f, 0f, 0f, 0f);

        // Env[47].z = 1 makes the height-based ambient attenuation a no-op for any height (verified in chara_metal). .x/.y are
        // inert: the one forward consumer (Mt_Skin's material_prog10336, temp_348) has the same z-protected shape, which at
        // z = 1 collapses to fma(temp_29, 0.5, 0.5) independent of .x/.y. temp_N numbering is not stable across
        // re-preparations, so re-verify by shape.
        b.SetSlot(Slots.AmbientHeightAttenuation, 0f, 0f, 1f, 0f);
        // Env[70].zw: .z nonzero collapses the alpha term to 0 rather than NaN; .w unused here.
        b.SetComponent(Slots.Unknown70, 2, 1f);
        b.SetComponent(Slots.Unknown70, 3, 0f);
        // Env[81]: volume-mask tint, driven by the palette's VolumeMaskColor/Intensity (0 = inert).
        b.SetVec3(Slots.VolumeMaskTint, volumeMaskColor, volumeMaskIntensity);

        b.SetComponent(Slots.ShadowDepthBias, 1, 0.0005f);
        b.SetComponent(Slots.ShadowMapDimensions, 0, BitConverter.Int32BitsToSingle(shadowMapSize));
        b.SetComponent(Slots.ShadowMapDimensions, 1, BitConverter.Int32BitsToSingle(shadowMapSize));

        // A pow-exponent slot left at 0 becomes NaN when a forward shader evaluates pow(0, 0) through exp2(log2(x)*k); 1.0 is NaN-free and, for the two fog-damp dual-use slots, the correct "no curve".
        foreach (var (slot, comp) in PowExponentSlots)
        {
            if (b.GetComponent(slot, comp) == 0f)
                b.SetComponent(slot, comp, 1f);
        }

        return env;
    }

    public byte[] ToByteArray() => _block.ToByteArray();
}
