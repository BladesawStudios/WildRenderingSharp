using System.Numerics;

namespace WildRenderingSharp.Profiles.Totk.Atmosphere;

/// <summary>
/// The real <c>agl::pfx::ColorCorrection</c> config from <c>postfx/master_field.baglccr</c> - the game's final grade, applied after
/// <c>agl_hdr_compose</c>.
/// </summary>
public sealed class ColorCorrectionPostFx
{
    public bool Enable = true;
    public float Hue;
    public float Saturation = 1f;
    public float Brightness = 1f;
    public float Gamma = 1f;

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

    public bool IsIdentity =>
        !Enable || (Hue == 0f && Saturation == 1f && Brightness == 1f && Gamma == 1f && !ToycamEnable);
}
