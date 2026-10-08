using System.Numerics;

namespace WildRenderingSharp.Rendering;

/// <summary>The live-tunable lighting and exposure state of a viewing session, in terms any game's profile can read.</summary>
public class LightingContext
{
    public BackgroundMode Background { get; set; } = BackgroundMode.Color;

    /// <summary>The flat colour <see cref="BackgroundMode.Color"/> clears to: a near-black rather than pure black.</summary>
    public Vector3 BackgroundColor { get; set; } = new(0.000075f, 0.00008f, 0.0001f);

    /// <summary>Sun elevation in radians; with <see cref="SunAzimuth"/> it gives the sun direction.</summary>
    public float SunElevation { get; set; } = 0.6f;
    public float SunAzimuth { get; set; }

    /// <summary>
    /// Multiplies the HDR frame before the tonemap. Chosen by hand: the games author 1.0 against lighting this renderer lacks
    /// (local lights, probe IBL).
    /// </summary>
    public float Exposure { get; set; } = 2.5f;

    public float AmbientScale { get; set; } = 1.0f;
    public float MidScale { get; set; } = 1.0f;
    public float HighlightScale { get; set; } = 1.0f;

    /// <summary>Correction for the lit path being about 5.5x too bright; unlike <see cref="Exposure"/> it leaves emission alone.</summary>
    public float SceneGain { get; set; } = 1f / 5.5f;

    public float EmissionScale { get; set; } = 1.0f;

    /// <summary>Multiplies the environment's authored bloom; 0 turns it off.</summary>
    public float BloomIntensity { get; set; } = 1.0f;

    public bool IconCaptureMode { get; set; }

    /// <summary>Fills the light pre-pass with a synthetic sun-plus-ambient term. The games accumulate local lights only, so this is off.</summary>
    public bool SyntheticLightPrePass { get; set; }

    public bool ShowGrid { get; set; } = true;
}
