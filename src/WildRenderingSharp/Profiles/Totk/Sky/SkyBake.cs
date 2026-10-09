using WildRenderingSharp.Assets;
using System.Numerics;
using WildRenderingSharp.Pipeline.Frame;
using WildRenderingSharp.Profiles.Totk.Atmosphere;

namespace WildRenderingSharp.Profiles.Totk.Sky;

/// <summary>Runs the atmosphere precompute once per palette.</summary>
public sealed class SkyBake(FrameServices services) : IDisposable
{
    readonly SkyPrecomputePass _precompute = new(services.Gl, services.Programs);
    readonly Vector3 _sun = Vector3.UnitY;

    string _key = "\0never";

    public uint BakedInscatter => _precompute.BakedInscatter;

    public void Ensure(SkyPostFx postFx, EnvPalette? palette, string? paletteName, float tint)
    {
        float tintStep = MathF.Round(Math.Clamp(tint, 0f, 1f) * 20f) / 20f;
        string key = $"{paletteName}|{tintStep:F2}";
        if (key == _key || !_precompute.Available)
            return;

        // A palette may set Rayleigh to zero ("no blue sky"), so the amplifier is floored to keep the solve integrable.
        float rayleigh = MathF.Max(0.02f, palette?.SkyRayleighAmplifier ?? 1f);
        float mie = palette is { SkyMieAmplifier: > 0f } ? palette.SkyMieAmplifier : 1f;
        _key = key;
        Console.WriteLine($"[SkyBake] baking sky LUT for palette '{key}' (rayleigh x{rayleigh:G4}, mie x{mie:G4})");

        _precompute.Run(services.Resources, postFx, _sun, rayleigh, mie, TintColor(palette, tintStep), SkyLook.From(palette));
        GLDiagnostics.CheckPass(services.Gl, "sky precompute");
        _precompute.Verify();
    }

    // The fog colour's hue, normalised to its brightest channel. Fog is the better source than the background colour: a palette
    // with no Rayleigh scattering has a sky that is effectively dense fog, and its fog colour is the red or amber the
    // background colour only approximates.
    static Vector3 TintColor(EnvPalette? palette, float tintStep)
    {
        Vector3 fog = palette?.FogColor ?? Vector3.One;
        float fogMax = MathF.Max(fog.X, MathF.Max(fog.Y, fog.Z));
        Vector3 hue = fogMax > 1e-5f ? fog / fogMax : Vector3.One;
        return Vector3.Lerp(Vector3.One, hue, tintStep);
    }

    public void Dispose() => _precompute.Dispose();
}
