namespace WildRenderingSharp.Pipeline;

/// <summary>
/// The real <c>agl_cloud</c> "Common" uniform block, captured verbatim from a real frame of the
/// actual game (Ryujinx + RenderDoc, Colour Pass #40's near-dome draw, buffer 24637 bytes
/// 6400-7168), used as the BASELINE that <see cref="CloudDomePass"/> then overlays live
/// ROM-derived values onto.
///
/// WHY A CAPTURED BASELINE RATHER THAN AN ALL-ZERO BUFFER: this block is 48 vec4 slots and the real
/// shader divides by several of them. Two are fatal - the fragment shader opens with
/// <c>1.0 / fp_c3.data[5].w</c> (a UV distance normaliser) and later takes
/// <c>1.0 / fp_c3.data[26].w</c>. Left at zero those become infinity, the infinities reach the
/// texture-coordinate maths, and the whole cloud resolves to NaN, which clamps to nothing
/// on screen. Every slot whose real source has since been identified IS overwritten from live ROM
/// data (see <see cref="CloudDomePass.BuildCommonBlock"/>); the residue kept from here is the
/// handful of genuinely-unidentified runtime constants, which are the same for every user and every
/// palette, so keeping the real captured value is strictly better than guessing or zeroing.
///
/// This is 768 bytes of engine constants, NOT game content - it is not model, texture, audio or
/// shader data, and it is not derived from a ROM the user might not own (a user with no capture of
/// their own still gets a working cloud shader out of it). It exists as a checked-in constant for
/// exactly the same reason a hardcoded default anywhere else does: the alternative is a broken
/// render.
///
/// Slot-by-slot identifications live in <see cref="CloudDomePass.BuildCommonBlock"/>. Notably still
/// unidentified and therefore inherited from here: slots 0-1 (per-frame values of unknown meaning),
/// 4.y/4.z (the alpha multiply/threshold pair, which do NOT equal the AAMP's own mAlphaMul/
/// mAlphaThreshold for either layer - runtime-modulated somehow), 5.y/5.w (5.w being the fatal
/// divisor above), 9/13/17/21.x (the placement-point proximity fades - real, but they need
/// EffectCloudPlacementPoints data WildRenderingSharp doesn't load), 25.x, 33.y (a camera-relative sky height),
/// 35-41 odds and ends, 46.
/// </summary>
static class CloudUboBaseline
{
    /// <summary>The captured 768-byte "Common" block. See the class remarks before changing anything here.</summary>
    public static byte[] Common() => Convert.FromHexString(string.Concat(CommonHex));

    static readonly string[] CommonHex =
    [
        "00688A44829E313F0AEF9D3EC71EDF3E6F01A9BDA95084BF5D3AA63F00008040",
        "000000410000003F0000003FCDCC4C3DD044D8BB0AD7233CCDCCCC3D6666A63F",
        "CDCC4C3D1C6AF93F928D293F9A99993F0000003F3333B33E3333333FB8EBC53F",
        "87711DBDDA1CA03D5321FF3EF63CBA3E000000416666E63FCDCC4C3F9A99193E",
        "9A99193E3333333F3333733F000080BF6093773E000000000000000000000000",
        "0000000000000000000000000000000000000000000000000000000000000000",
        "0000000000000000000000000000000020A67B3F000000000000000000000000",
        "0000000000000000000000000000000000000000000000000000000000000000",
        "000000000000000000000000000000003333333F000000000000000000000000",
        "0000000000000000000000000000000000000000000000000000000000000000",
        "0000000000000000000000000000000061BEA3BE000000000000000000000000",
        "0000000000000000000000000000000000000000000000000000000000000000",
        "00000000000000000000000000000000CC6BC93E000030408C2EBA3E0000C040",
        "6666663F8FC2753C0000403F9A99593F00000041000000000000000000000000",
        "C2A8F43EE68CF83EDF4FDD3E3333733FCDCC4C3F7F6A3C3F295C0F3FCDCC4C3F",
        "EFC9833EC342AD3EE86AAB3E6666663F0000803F1F856B3F3333333F00000000",
        "0008CF460000000000000000000000000000000054DBEB450000000000000000",
        "00000000000000000008CF46000000000000000000000000000000000000803F",
        "8FC2153F0000803F04564E3FD55FB73D0E745A3B000000000000000009A9A642",
        "000000000000003F000000000000000000000000000000000000000000000000",
        "65CF0B3800000000000000000000000000002042000020409A99593FD2880DBC",
        "4F0417BC0C622CBF4F3F3DBF0000803F0000803F0000803E0000C04000000040",
        "0000803F0000803E0000000000000000000000000000803F0000000000000000",
        "0050C3460000C842000000000000000000000000000000000000000000000000",
    ];

    /// <summary>The real <c>cZOffsetParam</c> (View block slot 12.x) from the same capture - a per-frame depth-bias value whose own source was never identified, so the real observed constant stands in.</summary>
    public const float ZOffsetParam = 0.4777379035949707f;
}
