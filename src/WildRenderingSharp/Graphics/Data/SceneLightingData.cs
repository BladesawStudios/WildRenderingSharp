using System.Numerics;

namespace WildRenderingSharp.Graphics.Data;

/// <summary>Everything a profile needs to describe the scene's lighting to its shaders.</summary>
public readonly record struct SceneLightingData(
    Vector3 SunDirView, Vector3 SunDirWorld, Vector3 SunColor,
    Vector3 HemiSky, Vector3 HemiGround,
    Vector3 VolumeMaskColor, float VolumeMaskIntensity,
    int ShadowMapSize, float MidScale, float HighlightScale);
