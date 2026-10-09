using WildRenderingSharp.Graphics;
﻿using WildRenderingSharp.Assets;
using System.Numerics;
using WildRenderingSharp.Gpu;
using WildRenderingSharp.Pipeline.Frame;
using WildRenderingSharp.Profiles.Totk.Atmosphere;
using WildRenderingSharp.Profiles.Totk.Atmosphere.Clouds;
using WildRenderingSharp.Profiles.Totk.Sky;
using WildRenderingSharp.Profiles.Totk.Sky.Clouds;
using WildRenderingSharp.Profiles.Totk.Sky.PostFx;
using WildRenderingSharp.Profiles.Totk.Sky.Precompute;
using WildRenderingSharp.Rendering.Lighting;

namespace WildRenderingSharp.Profiles.Totk.Stages;

/// <summary>
/// Paints the background into the HDR image before the resolve, which only writes pixels its pass-ID mask claims: the plain
/// background, then the game's sky shader over it, the sun and moon sprites, and the cloud dome.
/// </summary>
public sealed class SkyStage(StageServices services, SkyBake bake) : IFrameStage, IDisposable
{
    readonly BackgroundPass _background = new(services.Gl);
    readonly SkyPostFxPass _skyPostFx = new(services.Gl, services.Programs);
    readonly GroundPass _ground = new(services.Gl);
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
        {
            DrawSky(frame);
            DrawGround(frame);
        }
        if (settings.ShowSun || settings.ShowMoon)
            DrawBodies(frame);
        if (settings.UseRealCloudDome)
            DrawClouds(frame);
    }

    // The game's sky shader sampling the baked atmosphere. The table is in the game's own units, so its intensity is scaled to
    // land the brightest texel above 1 before the tonemap, which has the headroom to bring it down; the palette's own sky
    // brightness (relative to its default of 5) then makes night palettes darker. The ground colour is mixed in unscaled by the
    // shader, so it arrives pre-scaled.
    // The game multiplies its raw table by 1 (Context[13].x in a capture). This renderer's lit path is scaled down by SceneGain, and
    // the real skybin's high-sun zenith (0.69, 1.08, 1.28) sits at about 0.55-0.6 of this bake's, so the sky takes the same scale.
    const float SkyGain = 0.6f;

    void DrawSky(FrameContext frame)
    {
        var lighting = frame.Lighting;
        var environment = frame.TotkEnvironment();
        var settings = environment.Settings;
        var palette = environment.Palette;
        var cam = frame.Cam;

        float skyUnit = lighting.SceneGain * SkyGain * palette.BgDifIntensity / 5.0f;
        float intensity = settings.AtmosphereIntensity * skyUnit;
        Vector3 hazeColor = palette.FogColor * intensity;
        var fog = settings.UseSkyFog
            ? SkyPostFxPass.Resolve(palette, environment.SkyPostFx, intensity, settings.SkyFogStrength, settings.SkyFogNormaliseHue)
            : default;

        _skyPostFx.Run(services.Resources, frame.Targets, frame.Targets.Final, bake.BakedInscatter,
            GpuMatrix.Rows(cam.ViewInv, 3), cam.Aspect, cam.TanHalfFovY, frame.SunWorld, environment.SkyPostFx, intensity, hazeColor,
            settings.SkyHorizonHaze, fog);
        GLDiagnostics.CheckPass(services.Gl, "real sky postfx");
    }

    void DrawGround(FrameContext frame)
    {
        var cam = frame.Cam;
        _ground.Run(services.Resources, frame.Targets, frame.Targets.Final, GpuMatrix.Rows(cam.ViewInv, 3), cam.Aspect, cam.TanHalfFovY,
            frame.HemiGround * frame.Lighting.SceneGain * frame.TotkEnvironment().Palette.BgDifIntensity / 5.0f);
        GLDiagnostics.CheckPass(services.Gl, "sky ground");
    }

    void DrawBodies(FrameContext frame)
    {
        var lighting = frame.Lighting;
        var environment = frame.TotkEnvironment();
        var settings = environment.Settings;
        var palette = environment.Palette;
        var cam = frame.Cam;

        // The disc is the sun seen through the atmosphere, reddening as it nears the horizon. Relative to the sun overhead, whose colour is the
        // palette's own SkySunColor, so a high sun is not tinted by an atmosphere it has barely crossed.
        float rayleighAmplifier = MathF.Max(0.02f, palette.SkyRayleighAmplifier);
        float mieAmplifier = palette.SkyMieAmplifier > 0f ? palette.SkyMieAmplifier : 1f;
        Vector3 sunHue = SunTransmittance.Colour(environment.SkyPostFx, rayleighAmplifier, mieAmplifier, lighting.SunElevation)
            / Vector3.Max(SunTransmittance.Colour(environment.SkyPostFx, rayleighAmplifier, mieAmplifier, MathF.PI / 2f), new Vector3(1e-4f));
        float sunPeak = MathF.Max(sunHue.X, MathF.Max(sunHue.Y, sunHue.Z));
        if (sunPeak > 1e-6f) sunHue /= sunPeak;
        _skyBody.Run(services.Resources, frame.Targets, frame.Targets.Final, GpuMatrix.Rows(cam.ViewInv, 3), cam.Aspect, cam.TanHalfFovY,
            new SkyBodyPass.Params(
                SunDir: frame.SunWorld,
                MoonDir: SunDirection.FromElevationAzimuth(settings.MoonElevation, settings.MoonAzimuth),
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

        float seconds = _cloudDome.Advance(settings.AnimateClouds);
        var layers = ResolveCloudLayers(environment, seconds, frame.Camera.Eye.Y);

        _cloudDome.Run(services.Resources, frame.Targets, palette, environment.CloudPostFx.Shared, layers, seconds,
            cam, frame.Camera.Eye, frame.SunWorld,
            settings.CloudBrightness, lighting.Exposure, settings.CloudFade, palette.FogColor,
            settings.CloudResolutionScale, bake.BakedInscatter);
        GLDiagnostics.CheckPass(services.Gl, "cloud dome");
    }

    // The layers the weather leaves visible, far to near. Without the weather's data only the first layer's baseline is drawn.
    static List<CloudDomePass.Layer> ResolveCloudLayers(TotkEnvironment environment, float seconds, float altitude)
    {
        var settings = environment.Settings;
        var weather = environment.CloudWeather;
        var palette = environment.Palette;
        var baseline = environment.CloudPostFx.Layers;
        int set = Math.Clamp(settings.CloudWeatherSet, 0, CloudWeather.WeatherCount - 1);

        var resolved = new List<(int Index, CloudPostFxLayer Layer)>();
        for (int i = 0; i < CloudWeather.LayerCount; i++)
        {
            if (!settings.CloudLayerEnabled[i])
                continue;
            var layer = weather == CloudWeather.Empty
                ? (i == 0 ? baseline[0] : null)
                : CloudLayerResolver.Resolve(baseline[i], weather.Motion[i],
                    i < CloudWeather.LookLayerCount ? weather.Looks[set, i] : null, settings.CloudWind, seconds, altitude);
            if (layer is not null)
                resolved.Add((i, layer));
        }

        // The palette authors two colour sets: the first layer's, and the one the others share.
        var colours = new[] { palette.Cloud0, palette.Cloud1 };
        return resolved
            .OrderByDescending(r => r.Layer.SkyHeight)
            .Select(r =>
            {
                int own = Math.Min(r.Index, 1);
                return new CloudDomePass.Layer(r.Layer,
                    colours[own].Present ? colours[own] : colours[own ^ 1].Present ? colours[own ^ 1] : CloudDomePass.FallbackCloudLayer(r.Layer));
            })
            .ToList();
    }

    public void Dispose()
    {
        _background.Dispose();
        _skyPostFx.Dispose();
        _ground.Dispose();
        _skyBody.Dispose();
        _cloudDome.Dispose();
    }
}
