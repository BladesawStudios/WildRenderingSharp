using System.Numerics;

namespace WildRenderingSharp.Graphics;

/// <summary>The light slots at the start of <c>gsys_environment</c>, which both games lay out the same way, and the write that fills them.</summary>
public static class GsysEnvironment
{
    public const int Ambient = 0;
    public const int HemiSky = 1;
    public const int HemiGround = 2;
    public const int HemiDirection = 3;
    public const int LightDirection0 = 4;
    public const int LightColor0 = 5;
    public const int LightSpecularColor0 = 6;
    public const int LightDirection1 = 7;
    public const int LightColor1 = 8;
    public const int LightSpecularColor1 = 9;

    // The sun direction points toward the sun in view space, and the shaders take the direction the light travels, so it is negated.
    public static void WriteLights(UboWriter block, Vector3 sunDirView, Vector3 sunColor, Vector3 hemiSky, Vector3 hemiGround)
    {
        block.Set(Ambient, 0.10f, 0.11f, 0.13f, 1f);
        block.Set(HemiSky, hemiSky, 1f);
        block.Set(HemiGround, hemiGround, 1f);
        block.Set(LightDirection0, -sunDirView, 1f);
        block.Set(LightColor0, sunColor, 1f);
        block.Set(LightSpecularColor0, sunColor, 1f);
        block.Set(LightDirection1, 0f, -1f, 0f, 0f);
        block.Set(LightColor1, 0f, 0f, 0f, 1f);
        block.Set(LightSpecularColor1, 0f, 0f, 0f, 1f);
    }
}
