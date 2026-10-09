using System.Numerics;
using Silk.NET.OpenGL;
using WildRenderingSharp.Gpu;
using WildRenderingSharp.Pipeline.Passes;
using WildRenderingSharp.Pipeline.Resources;
using WildRenderingSharp.Pipeline.Targets;
using WildRenderingSharp.Profiles.Totk.Atmosphere.Clouds;
using WildRenderingSharp.Profiles.Totk.Atmosphere.Palettes;
using WildRenderingSharp.Rendering.Lighting;
using WildRenderingSharp.Shaders;

namespace WildRenderingSharp.Profiles.Totk.Sky;

/// <summary>
/// Fills <c>targets.Final</c> with the requested <see cref="BackgroundMode"/> before the deferred resolve, which only writes pixels
/// its pass-ID mask claims, so whatever is left here survives wherever no actor covers.
/// </summary>
internal sealed class BackgroundPass : IDisposable
{
    readonly GL _gl;
    readonly uint _skyProgram;

    public const float SkyColorAnchor = 0.03f;

    // "Up" is worldDir.y. Everything keys off worldDir alone, a pure function of the
    // camera's rotation, so the sky sits at infinity and only turning the camera moves it.
    static readonly string SkyFragmentSource = GlslFiles.Load("Totk/Sky/Background/Sky.frag");

    public BackgroundPass(GL gl)
    {
        _gl = gl;
        _skyProgram = GLProgramBuilder.Build(gl, FullscreenShaders.Vertex450, SkyFragmentSource, "background_sky");
    }

    public void Run(GLResourceCache resources, RenderTargets targets, BackgroundMode mode, Vector3 color,
        Vector3 sunWorld, EnvPalette palette, Matrix4x4 viewInv, Vector2 tanHalf, float sceneGain,
        SkyPostFx? postfx = null, CloudPostFx? cloudPostFx = null, float atmosphereIntensity = 1f)
    {
        postfx ??= SkyPostFx.Default;
        cloudPostFx ??= CloudPostFx.Default;
        targets.BindColorTarget(targets.Final);
        _gl.Disable(EnableCap.DepthTest);

        switch (mode)
        {
            case BackgroundMode.Transparent:
                _gl.ClearColor(0f, 0f, 0f, 0f);
                _gl.Clear(ClearBufferMask.ColorBufferBit);
                break;

            case BackgroundMode.Sky:
                // Anchored to the SceneGain-corrected BgDifIntensity, the magnitude the lit path targets.
                float intensityScale = palette.BgDifIntensity * sceneGain * SkyColorAnchor;
                Vector3 sunDiscColor = palette.SkySunColorNoUse
                    ? Vector3.Zero
                    : Vector3.Min(palette.SkySunColor * palette.SkySunColorIntensity * sceneGain, new Vector3(3f));

                // The palette's Mie asymmetry wins when authored; otherwise the postfx rendering default,
                // since 0 would give an isotropic lobe with no sun glow.
                float mieG = palette.SkyMieSymmetrical != 0f ? palette.SkyMieSymmetrical : postfx.MieSymmetricalPropRendering;

                _gl.UseProgram(_skyProgram);
                _gl.SetMat4(_skyProgram, "uViewInv", viewInv);
                _gl.SetVec2(_skyProgram, "uTanHalf", tanHalf);
                _gl.SetVec3(_skyProgram, "uSunWorld", sunWorld);

                // Atmosphere parameters from master_field.baglsky. The postfx coefficients are the static
                // baseline; the palette's rayleigh and mie amplifiers are the per-scene layer on top
                // (Rayleigh 1 at noon, 0.25 at night, 0 under a blood moon; Mie 12, 0 and 256). The reference
                // amplifiers are the postfx defaults, which is the neutral point.
                const float ReferenceRayleighAmplifier = 1.0f;
                const float ReferenceMieAmplifier = 12.0f;
                float rayleighAmplifier = MathF.Max(0f, palette.SkyRayleighAmplifier) / ReferenceRayleighAmplifier;
                float mieAmplifier = MathF.Max(0f, palette.SkyMieAmplifier) / ReferenceMieAmplifier;

                _gl.SetVec3(_skyProgram, "uRayleighCoeff", postfx.RayleighScatteringCoeff * rayleighAmplifier);
                _gl.SetFloat(_skyProgram, "uRayleighScaleHeightKm", postfx.RayleighBaseHeight);
                _gl.SetFloat(_skyProgram, "uMieCoeff", postfx.MieScatteringCoeff * mieAmplifier);
                _gl.SetFloat(_skyProgram, "uMieScaleHeightKm", postfx.MieBaseHeight);
                _gl.SetFloat(_skyProgram, "uMieG", mieG);
                // No sea level exists in this coordinate system, so the camera height is 0.
                _gl.SetFloat(_skyProgram, "uCameraHeightKm", 0f);
                // Incoming sunlight uses the palette's sun colour (shared with the disc) scaled by the live
                // intensity. Not floored, so 0 means no contribution.
                Vector3 sunColorSrc = palette.SkySunColorNoUse ? Vector3.Zero : palette.SkySunColor * palette.SkySunColorIntensity;
                Vector3 sunIntensity = sunColorSrc * intensityScale * 40f * atmosphereIntensity;
                _gl.SetVec3(_skyProgram, "uSunIntensity", sunIntensity);

                // Fog and ground are scaled by the same factors as the integral, so the intensity slider at 0 removes them too.
                float ambientCalib = intensityScale * atmosphereIntensity;
                _gl.SetVec3(_skyProgram, "uFogColor", palette.FogColor * 0.35f * ambientCalib);
                _gl.SetFloat(_skyProgram, "uFogHorz", postfx.ScatterFogHorz);
                _gl.SetVec3(_skyProgram, "uGroundColor", postfx.GroundColor * ambientCalib);
                _gl.SetVec3(_skyProgram, "uSunDiscColor", sunDiscColor);
                _gl.SetFloat(_skyProgram, "uSunSize", postfx.RenderSunSize);
                _gl.SetFloat(_skyProgram, "uSunLerp", postfx.RenderSunLerp);

                // Anchors both cloud layers' roughly unit-scale colours to the sky's magnitude.
                float cloudBrightness = MathF.Min(0.6f, palette.BgDifIntensity * sceneGain * 0.35f);
                _gl.SetFloat(_skyProgram, "uCloudBrightness", cloudBrightness);
                SetCloudLayer(0, palette.Cloud0, cloudPostFx.Layer0);
                SetCloudLayer(1, palette.Cloud1, cloudPostFx.Layer1);

                // Fades in once the sun sets; the palettes author no star toggle.
                float starBrightness = MathF.Min(0.5f, MathF.Max(0f, -sunWorld.Y) * 1.5f);
                _gl.SetFloat(_skyProgram, "uStarBrightness", starBrightness);

                resources.DrawFullscreenTriangle();
                break;

            default: // Color
                _gl.ClearColor(color.X, color.Y, color.Z, 1f);
                _gl.Clear(ClearBufferMask.ColorBufferBit);
                break;
        }
    }

    // Uploads one layer's uniforms: the palette's colour, intensity and backlight fields plus the postfx CloudParamN alpha
    // threshold, multiplier and density (a separate, static source). index is 0 or 1.
    void SetCloudLayer(int index, EnvPalette.CloudLayer layer, CloudPostFxLayer postfxLayer)
    {
        string p = $"uCloud{index}";
        _gl.SetFloat(_skyProgram, p + "Active", layer.Present ? 1f : 0f);
        _gl.SetVec3(_skyProgram, p + "ColorBase", layer.ColorBase);
        _gl.SetVec3(_skyProgram, p + "ColorHilight", layer.ColorHilight);
        _gl.SetVec3(_skyProgram, p + "ColorShadow", layer.ColorShadow);
        _gl.SetVec3(_skyProgram, p + "ColorBackLight", layer.ColorBackLight);
        _gl.SetFloat(_skyProgram, p + "IntensityBase", layer.IntensityBase);
        _gl.SetFloat(_skyProgram, p + "IntensityHilight", layer.IntensityHilight);
        _gl.SetFloat(_skyProgram, p + "IntensityShadow", layer.IntensityShadow);
        _gl.SetFloat(_skyProgram, p + "BacklightPower", layer.BacklightPower);
        _gl.SetFloat(_skyProgram, p + "AlphaThreshold", postfxLayer.AlphaThreshold);
        _gl.SetFloat(_skyProgram, p + "AlphaMul", postfxLayer.AlphaMul);
        _gl.SetFloat(_skyProgram, p + "Density", postfxLayer.Density);
    }

    public void Dispose() => _gl.ReleaseProgram(_skyProgram);
}
