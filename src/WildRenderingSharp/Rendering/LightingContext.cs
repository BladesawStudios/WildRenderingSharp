using System.Numerics;

namespace WildRenderingSharp.Rendering;

/// <summary>The live-tunable lighting and exposure state of a viewing session, in terms any game's profile can read.</summary>
public class LightingContext
{
    public BackgroundMode Background { get; set; } = BackgroundMode.Color;

    public Vector3 BackgroundColor { get; set; } = new(0.000075f, 0.00008f, 0.0001f);

    public float SunElevation { get; set; } = 0.6f;
    public float SunAzimuth { get; set; }

    public float Exposure { get; set; } = 2.5f;

    public float AmbientScale { get; set; } = 1.0f;
    public float MidScale { get; set; } = 1.0f;
    public float HighlightScale { get; set; } = 1.0f;

    public float SceneGain { get; set; } = 1f / 5.5f;

    public float EmissionScale { get; set; } = 1.0f;

    public float BloomIntensity { get; set; } = 1.0f;

    public bool IconCaptureMode { get; set; }

    public bool SyntheticLightPrePass { get; set; }

    public bool ShowGrid { get; set; } = true;
}
