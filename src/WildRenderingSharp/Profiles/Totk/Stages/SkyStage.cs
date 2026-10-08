using WildRenderingSharp.Assets;
using System.Numerics;
using WildRenderingSharp.Pipeline.Frame;
using WildRenderingSharp.Profiles.Totk.Sky;
using WildRenderingSharp.Rendering;
using WildRenderingSharp.Profiles.Totk.Atmosphere;

namespace WildRenderingSharp.Profiles.Totk.Stages;

/// <summary>
/// Paints the background into the HDR image before the resolve, which only writes pixels its
/// pass-ID mask claims: the plain background, then the game's sky shader over it, the sun and moon
/// sprites, and the cloud dome.
/// </summary>
public sealed class SkyStage(FrameServices services, SkyBake bake) : IFrameStage, IDisposable
{
    readonly BackgroundPass _background = new(services.Gl);
    readonly SkyPostFxPass _skyPostFx = new(services.Gl, services.Programs);
    readonly SkyBodyPass _skyBody = new(services.Gl, services.Directories.SystemTextures);
    readonly CloudDomePass _cloudDome = new(services.Gl, services.Programs, services.Directories.SystemTextures);

    public void Run(FrameContext frame)
    {
        var lighting = frame.Lighting;
        var environment = frame.TotkEnvironment();
        var settings = environment.Settings;

        _background.Run(services.Resources, frame.Targets, lighting.Background, lighting.BackgroundColor, frame.SunWorld,
            environment.Palette, frame.Cam.ViewInv, frame.Cam.TanHalf, lighting.SceneGain, environment.SkyPostFx, environment.CloudPostFx,
            settings.AtmosphereIntensity);
        GLDiagnostics.CheckPass(services.Gl, "background");

        if (lighting.Background != BackgroundMode.Sky)
            return;

        if (settings.UseRealSkyShader && _skyPostFx.Available)
            DrawSky(frame);
        if (settings.ShowSun || settings.ShowMoon)
            DrawBodies(frame);
        if (settings.UseRealCloudDome)
            DrawClouds(frame);
    }

    /// <summary>
    /// The game's sky shader sampling the baked atmosphere. The table is in the game's own units, so
    /// its intensity is scaled to land the brightest texel above 1 before the tonemap, which has the
    /// headroom to bring it down; the palette's own sky brightness (relative to its default of 5)
    /// then makes night palettes darker. The ground colour is mixed in unscaled by the shader, so it
    /// arrives pre-scaled.
    /// </summary>
    void DrawSky(FrameContext frame)
    {
        var lighting = frame.Lighting;
        var environment = frame.TotkEnvironment();
        var settings = environment.Settings;
        var palette = environment.Palette;
        var cam = frame.Cam;

        float paletteBrightness = palette.BgDifIntensity / 5.0f;
        float intensity = settings.AtmosphereIntensity * paletteBrightness * settings.SkyHdrLevel
            / (SkyPrecomputePass.NormalisedPeak * MathF.Max(1e-4f, lighting.Exposure));
        Vector3 groundColor = palette.BgDifColor * intensity;
        var fog = settings.UseSkyFog
            ? SkyPostFxPass.Resolve(palette, environment.SkyPostFx, intensity, settings.SkyFogStrength, settings.SkyFogNormaliseHue)
            : default;

        _skyPostFx.Run(services.Resources, frame.Targets, frame.Targets.Final, bake.BakedInscatter,
            cam.ViewInv, cam.Aspect, cam.TanHalfFovY, frame.SunWorld, environment.SkyPostFx, intensity, groundColor,
            settings.SkyPaletteTint, fog);
        GLDiagnostics.CheckPass(services.Gl, "real sky postfx");
    }

    /// <summary>The sun and moon sprites, drawn after the sky and before the clouds so cloud occludes them.</summary>
    void DrawBodies(FrameContext frame)
    {
        var lighting = frame.Lighting;
        var environment = frame.TotkEnvironment();
        var settings = environment.Settings;
        var palette = environment.Palette;
        var cam = frame.Cam;

        // The sprite takes the palette's sun hue only; its brightness is the slider's.
        Vector3 sunHue = palette.SkySunColorNoUse ? Vector3.One : AmbientLighting.NormaliseHue(palette.SkySunColor);
        _skyBody.Run(services.Resources, frame.Targets, frame.Targets.Final, cam.ViewInv, cam.Aspect, cam.TanHalfFovY,
            new SkyBodyPass.Params(
                SunDirZUp: frame.SunWorld,
                MoonDirZUp: SunDirection.FromElevationAzimuth(settings.MoonElevation, settings.MoonAzimuth),
                SunColor: sunHue * settings.SunSpriteIntensity,
                MoonColor: Vector3.One * settings.MoonSpriteIntensity,
                SunAngularRadius: float.DegreesToRadians(settings.SunAngularRadiusDegrees),
                MoonAngularRadius: float.DegreesToRadians(settings.MoonAngularRadiusDegrees),
                MoonPhase: settings.MoonPhase,
                DrawSun: settings.ShowSun,
                DrawMoon: settings.ShowMoon));
        GLDiagnostics.CheckPass(services.Gl, "sky bodies");
    }

    void DrawClouds(FrameContext frame)
    {
        var lighting = frame.Lighting;
        var environment = frame.TotkEnvironment();
        var settings = environment.Settings;
        var palette = environment.Palette;
        var cam = frame.Cam;

        _cloudDome.Run(services.Resources, frame.Targets, palette, environment.CloudPostFx.Shared, environment.CloudPostFx.Layer0,
            cam.View, cam.Proj, frame.Camera.Eye, frame.SunWorld,
            settings.CloudBrightness, lighting.Exposure, settings.AnimateClouds, settings.CloudFade, palette.FogColor,
            settings.CloudResolutionScale, bake.BakedInscatter);
        GLDiagnostics.CheckPass(services.Gl, "cloud dome");
    }

    public void Dispose()
    {
        _background.Dispose();
        _skyPostFx.Dispose();
        _skyBody.Dispose();
        _cloudDome.Dispose();
    }
}
