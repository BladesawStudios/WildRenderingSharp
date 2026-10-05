namespace WildRenderingSharp.Rendering;

/// <summary>
/// Bakes an authored TexSrt (mode, scaleX, scaleY, rotation, translateX, translateY) into the 2x2
/// rotate-scale matrix + translation the compiled shader's <c>gsys_material</c> block actually
/// stores - the runtime twin of <c>ShaderLibrary.CompileTool.BuildMaterialUbo.TexSrtBake</c> (that
/// class's remarks carry the full Ghidra derivation: <c>nn::g3d2::MaterialObj::ConvertDirtyParams</c>'s
/// per-kind callback table, dispatcher 0x7100072448, mode 0 baker 0x7100072860, mode 1 baker
/// 0x7100072950). Duplicated rather than shared because <c>WildRenderingSharp</c> takes no dependency on
/// the offline BFRES/BFSHA tooling (see this project's own CLAUDE.md: "Parses no BFRES/BFSHA of its
/// own") - this is pure float math with no such dependency, so porting it here is cheaper than
/// inventing a shared assembly for one function.
///
/// <see cref="WildRenderingSharp.Rendering.MaterialAnimPose"/> needs this because a material-parameter
/// animation can drive just ONE sub-field of a TexSrt (a scroll anim touching only translateY, say)
/// - the untouched sub-fields have to come from the material's own authored baseline
/// (<c>MaterialUniformEntry.RawSrt</c>), and the whole six-value set has to be re-baked together
/// every frame, the same way the offline overlay does once at export time. Writing an animated
/// curve's raw float directly into the ALREADY-BAKED buffer (what this codebase did before the
/// static-overlay TexSrt fix landed) only happens to look right at the identity baseline
/// (scale=1, rotation=0, where the bake formula's pivot terms cancel out to exactly reproduce the
/// raw translate) and is wrong for any material with a real non-identity scale/rotation baked in.
/// </summary>
public static class TexSrtBake
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
                // Unconfirmed mode - fall back to the raw values rather than apply a formula that
                // was never verified against real content (see the offline twin's own remarks).
                outM0[0] = mode; outM0[1] = sx; outM0[2] = sy; outM0[3] = rot;
                outM1[0] = tx; outM1[1] = ty;
                return;
        }

        outM0[0] = m0x; outM0[1] = m0y; outM0[2] = m0z; outM0[3] = m0w;
        outM1[0] = m1x; outM1[1] = m1y;
    }
}
