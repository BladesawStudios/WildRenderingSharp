using System.Numerics;
using WildRenderingSharp.Rendering;

namespace WildRenderingSharp.Graphics;

/// <summary>What a frame's lighting needs from a game's environment data: the sun, the ambient, and the volume-mask tint.</summary>
public readonly record struct EnvironmentLighting(
    Vector3 SunColor, Vector3 HemiSky, Vector3 HemiGround, Vector3 VolumeMaskColor, float VolumeMaskIntensity);

/// <summary>The grade applied when a finished frame is presented.</summary>
public readonly record struct PresentGrade(float Saturation, float Brightness, float Gamma)
{
    public static PresentGrade Neutral => new(1f, 1f, 1f);
}

/// <summary>A game's per-frame environment data (palette, sky, clouds, grade).</summary>
public interface IFrameEnvironment
{
    EnvironmentLighting ResolveLighting(LightingContext lighting);

    PresentGrade PresentGrade { get; }
}
