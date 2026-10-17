using WildRenderingSharp.Graphics.Ubos;

namespace WildRenderingSharp.Profiles.Totk.Ubos;

/// <summary>
/// TotK's <c>gsys_environment</c>, decompiled as <c>fp_c9</c>. The light slots are in <see cref="GsysEnvironment"/>; beyond the fog groups
/// only fields with a confirmed read site are named.
/// </summary>
static class TotkEnvironmentLayout
{
    public static readonly UboSpec Spec = new("gsys_environment", TotkBindings.Environment, 1328);

    public const int FogColor = 10;                           // +160  fog group 0 (view-space)
    public const int FogDir = 11;                             // +176  .xyz, +188 cFogStart
    public const int FogStartEndInvDamp = 12;                 // +192  .x cFogStartEndInv, .y cFogDamp
    public const int WorldFogColor = 13;                      // +208  fog group 1 (world-space)
    public const int WorldFogDir = 14;                        // +224  .xyz, +236 cWorldFogStart
    public const int WorldFogStartEndInvDamp = 15;            // +240
    public const int WorldFogMaskColor = 16;                  // +256  fog group 2 (world-space mask)   <- read by deferred passes
    public const int WorldFogMaskDir = 17;                    // +272  .xyz, +284 cWorldFogMaskStart    <- read
    public const int WorldFogMaskStartEndInvDamp = 18;        // +288                                   <- read
    public const int FogFxColor = 19;                         // +304  fog group 3 (view-space fx)      <- read
    public const int FogFxDir = 20;                           // +320  .xyz, +332 cFogFxStart           <- read
    public const int FogFxStartEndInvDamp = 21;               // +336                                   <- read
    public const int HemiDirWorld = 22;                       // +352
    public const int LightDir0World = 23;                     // +368                                   <- read
    public const int LightDir1World = 24;                     // +384
    public const int Unknown25 = 25;                          // +400  one further declaration, type not recovered
    public const int ScatterFogRamp = 26;                     // .x and .y: scale and bias of the ramp over view distance                       <- read by preshading_*
    public const int ScatterFogShape = 27;                    // .x attenuation, .y horizontal exponent, .z density                              <- read
    public const int AdhocFogRamp = 28;                       // .x and .y: scale and bias of the ramp over view distance                       <- read
    public const int AdhocFogShape = 29;                      // .x ground attenuation; .z sky attenuation, .w the share it removes at the zenith <- read
    public const int AdhocFogColor = 30;                      // .xyz the colour; .w the density                                                 <- read
    public const int LeafFogRamp = 67;                        // .x and .y the distances where the leaf pass's two blends start, .z where both are complete <- read by preshading_field_leaf
    public const int EntranceSkyLight = 74;                   // .xyz the ambient from above, the entrance pass blends it with the next by the normal's height <- read by preshading_field_entrance
    public const int EntranceGroundLight = 75;                // .xyz the ambient from below                                                                  <- read
    public const int EntranceHazeColor = 76;                  // .xyz the colour of the haze it adds, .w its strength                                          <- read
    public const int EntranceHazeShape = 77;                  // .xy scale and bias of its ramp, .z the exponent                                                <- read
    public const int HeightFade = 44;                        // .xy and .zw: scale and bias of two world-height ramps      <- read by preshading_chara
    public const int DarknessMapTransform = 45;               // the Depths darkness map's UV transform                    <- read
    public const int DarknessBlend = 46;                      // .x keeps the surface lit; .y and .z bound the darkness map <- read
    public const int ShadowFarFade = 48;                      // .xy for the nearer cascades, .zw for the last: scale and bias of the fade to lit  <- read
    public const int AmbientHeightAttenuation = 47;
    public const int ShadowDepthBias = 50;                    // .x the tangent offset at grazing light, in metres; .y the depth bias <- read
    public const int ShadowMapDimensions = 52;
    public const int ShadowTexel = 56;                        // .xy one cascade texel in uv                                <- read
    public const int ShadowNormalBlend = 57;                  // .x the G-buffer normal's share, .y the depth-derived one's, .z the grazing exponent <- read
    public const int Unknown70 = 70;
    public const int VolumeMaskTint = 81;

    // Slots a forward shader raises to a power, which a zero turns into NaN through exp2(log2(x) * k).
    public static readonly (int Slot, int Component)[] PowExponentSlots =
    [
        (18, 1), (21, 1), (27, 0), (27, 1), (29, 0), (29, 2), (57, 2), (57, 3), (70, 2), (77, 2),
    ];
}
