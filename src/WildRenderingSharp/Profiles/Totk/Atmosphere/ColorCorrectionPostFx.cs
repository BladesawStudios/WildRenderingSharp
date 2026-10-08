using System.Numerics;

namespace WildRenderingSharp.Profiles.Totk.Atmosphere;

/// <summary>
/// The real <c>agl::pfx::ColorCorrection</c> config from <c>postfx/master_field.baglccr</c> - the
/// game's final grade, applied after <c>agl_hdr_compose</c>.
/// </summary>
/// <remarks>
/// WildRenderingSharp never read this, which matters for any comparison against a screenshot: the game's
/// shipped image is graded and WildRenderingSharp's was not. The field that shows up most is
/// <c>saturation = 1.175</c> - the game is ~17.5% more saturated than its own raw render.
///
/// The <c>level</c> curve array is not implemented (AAMP curves need an interpolator); everything
/// else in the object is.
/// </remarks>
public sealed class ColorCorrectionPostFx
{
    public bool Enable = true;
    public float Hue;
    public float Saturation = 1f;
    public float Brightness = 1f;
    public float Gamma = 1f;

    /// <summary>When true the toycam grade runs BEFORE hue/saturation/brightness, per <c>order_toycam_hsb</c>.</summary>
    public bool OrderToycamHsb = true;

    public bool ToycamEnable;
    public Vector3 ToycamOffset1 = Vector3.Zero;
    public Vector3 ToycamOffset2 = Vector3.Zero;
    public Vector3 ToycamLevel1 = Vector3.One;
    public Vector3 ToycamLevel2 = Vector3.One;
    public float ToycamSaturation1 = 1f;
    public float ToycamSaturation2 = 1f;
    public float ToycamBrightness = 1f;
    public float ToycamContrast = 1f;
    public Vector3 ToycamMulColor = Vector3.One;

    public static readonly ColorCorrectionPostFx Default = new();

    /// <summary>True when this grade would visibly change anything - lets the pass be skipped entirely.</summary>
    public bool IsIdentity =>
        !Enable || (Hue == 0f && Saturation == 1f && Brightness == 1f && Gamma == 1f && !ToycamEnable);
}
