using System.Numerics;

namespace WildRenderingSharp.Rendering.Lighting;

/// <summary>The live-tunable lighting and exposure state of a viewing session, in terms any game's profile can read.</summary>
public class LightingContext
{
    public BackgroundMode Background { get; set; } = BackgroundMode.Color;

    public Vector3 BackgroundColor { get; set; } = new(0.000075f, 0.00008f, 0.0001f);

    public float SunElevation { get; set; } = 0.6f;
    public float SunAzimuth { get; set; }

    // SceneGain scales the lit scene by 1/5.5, so 5.5 would be the game's own exposure of 1; comparing against the game by eye, 4.72 matches it better.
    public float Exposure { get; set; } = 4.72f;

    public float AmbientScale { get; set; } = 1.0f;
    public float MidScale { get; set; } = 1.0f;
    public float HighlightScale { get; set; } = 1.0f;

    public float SceneGain { get; set; } = 1f / 5.5f;

    public float EmissionScale { get; set; } = 1.0f;

    public float BloomIntensity { get; set; } = 1.0f;

    public bool SyntheticLightPrePass { get; set; }

    // The baked lighting of every static object that has none of its own, 0 to 1. The game's is 1.
    public float DefaultBakeStrength { get; set; } = 1.0f;

    public bool ShowGrid { get; set; } = true;
}
