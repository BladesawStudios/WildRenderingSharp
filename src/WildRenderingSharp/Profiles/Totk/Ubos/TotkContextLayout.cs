using WildRenderingSharp.Graphics.Ubos;

namespace WildRenderingSharp.Profiles.Totk.Ubos;

/// <summary>TotK's <c>gsys_context</c>, decompiled as <c>fp_c4</c>. The camera slots are in <see cref="GsysContext"/>; the tags mark how each slot's meaning was established.</summary>
static class TotkContextLayout
{
    public static readonly UboSpec Spec = new("gsys_context", TotkBindings.Camera, 2368);

    public const int Unknown9 = 19;                           // decl 9
    public const int Unknown10 = 20;                          // decl 10
    public const int PrevView = 21;                           // decl 11, 3 rows                      [DERIVED]
    public const int PrevViewProj = 24;                       // decl 12, 4 rows                       [DERIVED]
    public const int PrevProj = 28;                           // decl 13, 4 rows                       [DERIVED]
    public const int PrevViewInv = 32;                        // decl 14, 3 rows                       [DERIVED]
    public const int FrustumPlanes = 35;                      // decl 15, 6 rows                       [DERIVED]
    public const int Unknown16 = 41;                          // decl 16
    public const int ShadowCascadeMatrices = 42;              // decl 17, 16 rows                      [DERIVED]
    public const int Unknown18 = 58;                          // decl 18
    public const int Unknown19 = 59;                          // decl 19
    public const int Unknown20 = 60;                          // decl 20, 4 rows
    public const int Unknown21 = 64;                          // decl 21
    public const int Unknown22 = 65;                          // decl 22
    public const int Unknown23 = 66;                          // decl 23 (ivec4)
    public const int Unknown24 = 67;                          // decl 24
    public const int LargeParamArray = 68;                    // decl 25, 56 rows                      [DERIVED]
    public const int PackedFlags = 124;                       // decl 26-29 packed into one slot (uint,uint,bool,bool)
    public const int Unknown30 = 125;                         // decl 30, 3 rows
    public const int AuxArray = 128;                          // decl 31, 16 rows                      [DERIVED]
    public const int Unknown32 = 144;                         // decl 32
    public const int PackedIds = 145;
    public const int ScreenSize = 146;
    public const int FullscreenQuadParams = 147;
}
