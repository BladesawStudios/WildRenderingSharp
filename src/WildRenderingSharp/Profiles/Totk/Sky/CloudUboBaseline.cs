namespace WildRenderingSharp.Profiles.Totk.Sky;

/// <summary>
/// The <c>agl_cloud</c> "Common" uniform block captured verbatim from a frame of the game (Ryujinx and RenderDoc, Colour Pass #40's
/// near-dome draw, buffer 24637 bytes 6400-7168), the baseline <see cref="CloudDomePass"/> overlays live ROM-derived values onto.
/// </summary>
/// <remarks>
/// <para>
/// A captured baseline rather than zeros: the block is 48 vec4 slots and the shader divides by several. Two are fatal: the fragment shader opens with <c>1.0 / fp_c3.data[5].w</c> (a UV distance
/// normaliser) and later takes <c>1.0 / fp_c3.data[26].w</c>. Left at zero they become infinity, reach the texture-coordinate maths, and the whole cloud resolves to NaN. Every slot whose source is
/// identified is overwritten from ROM data (see <see cref="CloudDomePass.BuildCommonBlock"/>); what remains from here are unidentified runtime constants, the same for every user and palette, so the
/// captured value beats guessing or zeroing.
/// </para>
/// <para>
/// These are 768 bytes of engine constants, not model, texture, audio or shader content, and not derived from a ROM the user might not own. Still unidentified and so inherited: slots 0-1 (per-frame
/// values of unknown meaning), 4.y/4.z (the alpha multiply and threshold, which equal neither layer's AAMP mAlphaMul/mAlphaThreshold, so they are runtime-modulated), 5.y/5.w (5.w is the fatal divisor),
/// 9/13/17/21.x (placement-point proximity fades, needing EffectCloudPlacementPoints data that is not loaded), 25.x, 33.y (a camera-relative sky height), 35-41 and 46.
/// </para>
/// </remarks>
static class CloudUboBaseline
{
    /// <summary>The captured 768-byte "Common" block. See the class remarks before changing it.</summary>
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

    /// <summary>
    /// The real <c>cZOffsetParam</c> (View block slot 12.x) from the same capture - a per-frame depth-bias value whose own source
    /// was never identified, so the real observed constant stands in.
    /// </summary>
    public const float ZOffsetParam = 0.4777379035949707f;
}
