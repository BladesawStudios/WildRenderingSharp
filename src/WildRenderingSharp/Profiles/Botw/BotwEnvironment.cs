using System.Numerics;
using WildRenderingSharp.Graphics.Contracts;
using WildRenderingSharp.Rendering.Lighting;

namespace WildRenderingSharp.Profiles.Botw;

/// <summary>BotW's per-frame environment. Fixed daylight until the game's own sky and weather data are read.</summary>
internal sealed class BotwEnvironment : IFrameEnvironment
{
    public Vector3 SunColor { get; set; } = new(1.6f, 1.5f, 1.35f);
    public Vector3 HemiSky { get; set; } = new(0.55f, 0.65f, 0.85f);
    public Vector3 HemiGround { get; set; } = new(0.25f, 0.22f, 0.18f);
    public Vector3 Background { get; set; } = new(0.35f, 0.5f, 0.75f);

    /// <summary>Shows one pre-shading buffer instead of the lit frame, to see what the game's passes produce; negative shows the frame.</summary>
    public int DebugPreShading { get; set; } = -1;

    public EnvironmentLighting ResolveLighting(LightingContext lighting) =>
        new(SunColor, HemiSky * lighting.AmbientScale, HemiGround * lighting.AmbientScale, Vector3.Zero, 0f);

    public PresentGrade PresentGrade => PresentGrade.Neutral;
}
