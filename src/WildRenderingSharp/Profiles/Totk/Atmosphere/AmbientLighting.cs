using System.Numerics;

namespace WildRenderingSharp.Profiles.Totk.Atmosphere;

/// <summary>Resolves a palette's hemisphere ambient (sky and ground) colours.</summary>
/// <remarks>
/// <para>
/// In the game the hemisphere ambient is an <c>agl::env</c> object, not a ResEnvPalette field: it lives in <c>Env/GameScene.Nin_NX_NVN.genvb.zs</c> (a SARC of AAMP
/// <c>.baglenv</c> files) as a <c>HemisphereLight</c>. The only ones authored are situational (<c>main_env.baglenv</c> has <c>hemi_inner</c> and <c>hemi_cave</c>, both flat
/// greys), so outdoors there is no stored colour: the sky ambient is integrated from the sky itself, which the runtime renders per frame from Rayleigh and Mie scattering and
/// this renderer does not.
/// </para>
/// <para>
/// So this approximates that integral, driven by the palette's own scattering parameters. The ambient lights every surface from every direction while the sun only lights
/// faces toward it, so a fixed ambient made every palette look alike (a blood-moon palette rendered blue). The two terms are the halves of a single-scattering sky, from the
/// fields the game feeds its sky shader. Rayleigh, the blue term, has a fixed hue (normalised (0.144, 0.398, 1.0), close to the hand-authored indoor-capture sky
/// (0.131, 0.304, 1.0)) and an amount from <c>SkyRParam_rayleigh_amplifier</c> (noon 1.0, night 0.25, blood moon 0.0). Mie, the forward-scattered haze, takes the sun's colour
/// (<c>SkySunColor</c>) and <c>SkyRParam_mie_amplifier</c> (noon 12, night 0, blood moon 256). Blending by the amplifiers gives noon as blue plus warm haze, night as dim
/// blue, and a blood moon (all Mie, deep red sun) red.
/// </para>
/// </remarks>
public static class AmbientLighting
{
    const float AmbientSkyScale = 1.80f;
    const float AmbientGroundScale = 1.20f;

    /// <summary>The Mie amplifier of the reference daylight palette, so a palette matching it contributes an equal share of haze and blue rather than an arbitrary one.</summary>
    const float ReferenceMieAmplifier = 12.0f;
    /// <summary>Likewise for Rayleigh: the reference daylight palette's own amplifier is the unit.</summary>
    const float ReferenceRayleighAmplifier = 1.0f;

    /// <param name="ambientScale">The ambient slider. The palette's <see cref="EnvPalette.AmbientScale"/> multiplies it rather than replacing it.</param>
    /// <param name="postfx">The <c>agl::pfx::Sky</c> baseline from romfs; null uses <see cref="SkyPostFx.Default"/>.</param>
    public static (Vector3 Sky, Vector3 Ground) ResolveHemisphereColors(EnvPalette palette, float ambientScale, SkyPostFx? postfx = null)
    {
        postfx ??= SkyPostFx.Default;
        float scale = ambientScale * palette.AmbientScale;

        // An explicit HemisphereLight always wins: it is the authored value, and how the StudioLight preset gets a neutral ambient.
        if (palette.HasHemiColors)
        {
            float hemiScale = palette.HemiIntensity * scale;
            return (palette.HemiSkyColor * hemiScale, palette.HemiGroundColor * hemiScale);
        }

        Vector3 hue = SkyHue(palette, postfx);
        return (hue * (AmbientSkyScale * scale), postfx.GroundColor * (AmbientGroundScale * scale));
    }

    /// <summary>The colour of the directional light hitting actors.</summary>
    /// <remarks>
    /// <para>
    /// The hue comes from <c>SkySunColor</c>, the palette's authored sun. Lighting actors with <c>BgDifColor * BgDifIntensity</c> was wrong: BgDif is the background diffuse (the
    /// sky's colour), and the two differ on strongly coloured suns. On <c>BloodyMoon_DarknessDragon</c> the sun is (1, 0.140, 0.158) but BgDifColor (1, 0.297, 0.214), more than
    /// twice the green, which lit the actor orange. At noon they nearly coincide, which is why it went unnoticed.
    /// </para>
    /// <para>
    /// The magnitude stays with BgDif: every exposure decision was calibrated against it, and <c>SkySunColorIntensity</c> is on a different scale (18 at noon against BgDifIntensity
    /// 9; 1 on the dragon palette against 2). It is the same hue-from-one-field, brightness-from-another split the sky tint uses for FogColor. <c>SkySunColorNoUse</c> falls back
    /// to BgDif's hue.
    /// </para>
    /// </remarks>
    public static Vector3 SunColor(EnvPalette palette)
    {
        Vector3 hue = palette.SkySunColorNoUse ? Normalize(palette.BgDifColor) : Normalize(palette.SkySunColor);
        var bg = palette.BgDifColor;
        float magnitude = MathF.Max(bg.X, MathF.Max(bg.Y, bg.Z)) * palette.BgDifIntensity;
        return hue * magnitude;
    }

    /// <summary>The sky's colour for this palette, normalised to a hue (max component 1) so the caller owns the magnitude. Rayleigh and Mie are weighted by the palette's amplifiers rather than summed, which would let a blood moon's 256x Mie blow the ambient out 20x.</summary>
    public static Vector3 SkyHue(EnvPalette palette, SkyPostFx? postfx = null)
    {
        Vector3 rayleigh = Normalize((postfx ?? SkyPostFx.Default).RayleighScatteringCoeff);

        float wRayleigh = MathF.Max(0f, palette.SkyRayleighAmplifier) / ReferenceRayleighAmplifier;
        float wMie = MathF.Max(0f, palette.SkyMieAmplifier) / ReferenceMieAmplifier;

        // With no sun colour there is no meaningful haze tint, so the sky is pure Rayleigh.
        Vector3 mie = palette.SkySunColorNoUse ? rayleigh : Normalize(palette.SkySunColor);
        if (palette.SkySunColorNoUse)
            wMie = 0f;

        float total = wRayleigh + wMie;
        if (total <= 1e-6f)
            return rayleigh; // a palette that scatters neither still needs a sky to bounce off

        return Normalize((rayleigh * wRayleigh + mie * wMie) / total);
    }

    /// <summary>A colour reduced to its hue, for callers that own the magnitude themselves (the sun sprite, the horizon fog colour).</summary>
    public static Vector3 NormaliseHue(Vector3 c) => Normalize(c);

    /// <summary>Scales a colour so its largest component is 1, leaving pure black alone. Hue only - magnitude is the caller's business.</summary>
    static Vector3 Normalize(Vector3 c)
    {
        float max = MathF.Max(c.X, MathF.Max(c.Y, c.Z));
        return max > 1e-6f ? c / max : c;
    }
}
