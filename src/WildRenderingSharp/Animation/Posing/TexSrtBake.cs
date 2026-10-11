namespace WildRenderingSharp.Animation.Posing;

/// <summary>
/// Bakes an authored TexSrt (mode, scaleX, scaleY, rotation, translateX, translateY) into the 2x2 rotate-scale matrix plus
/// translation the compiled shader's <c>gsys_material</c> block stores.
/// </summary>
internal static class TexSrtBake
{
    public static void Bake(int mode, float sx, float sy, float rot, float tx, float ty, Span<float> outM0, Span<float> outM1)
    {
        float cos = MathF.Cos(rot);
        float sin = MathF.Sin(rot);

        float m0x = sx * cos, m0y = -sy * sin, m0z = sx * sin, m0w = sy * cos;
        float m1x, m1y;

        switch (mode)
        {
            case 0:
            {
                // 0x7100072860 - Maya-style, pivot at UV centre (0.5, 0.5).
                float sinHalf = MathF.FusedMultiplyAdd(sin, 0.5f, -0.5f);
                m1x = sx * ((cos * -0.5f - sinHalf) - tx);
                m1y = sy * (MathF.FusedMultiplyAdd(cos, -0.5f, sinHalf) + ty) + 1.0f;
                break;
            }
            case 1:
            {
                // 0x7100072950 - 3dsMax-style, pivot at UV centre, different translate handedness.
                m1x = sx * sin * (ty - 0.5f) - sx * cos * (tx + 0.5f) + 0.5f;
                m1y = sy * sin * (tx + 0.5f) + sy * cos * (ty - 0.5f) + 0.5f;
                break;
            }
            default:
                // Unconfirmed mode: fall back to the raw values rather than apply a formula never verified against real content (see the offline twin).
                outM0[0] = mode; outM0[1] = sx; outM0[2] = sy; outM0[3] = rot;
                outM1[0] = tx; outM1[1] = ty;
                return;
        }

        outM0[0] = m0x; outM0[1] = m0y; outM0[2] = m0z; outM0[3] = m0w;
        outM1[0] = m1x; outM1[1] = m1y;
    }
}
