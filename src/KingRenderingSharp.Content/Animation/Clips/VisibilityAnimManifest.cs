using System.Text.Json;
using System.Text.Json.Serialization;
using KingRenderingSharp.Content.Animation.Posing;
using KingRenderingSharp.Core.Storage;

namespace KingRenderingSharp.Content.Animation.Clips;

/// <summary>
/// A visibility animation: which bones, or which materials, are drawn over its frames.
/// Loaded from <c>&lt;Model&gt;.&lt;AnimName&gt;.visanim.json</c> in the cache.
/// </summary>
public sealed class VisibilityAnimManifest : IAnimClip
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";

    /// <summary>"Bone" when the names are bones, whose shapes are hidden with them, or "Material" when they are materials.</summary>
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("frame_count")] public int FrameCount { get; set; }
    [JsonPropertyName("loop")] public bool Loop { get; set; }
    [JsonPropertyName("entries")] public List<VisibilityAnimEntry> Entries { get; set; } = [];

    public bool IsBone => string.Equals(Kind, "Bone", StringComparison.Ordinal);


}

/// <summary>One bone or material of a visibility animation: its visibility before any key and the curve that drives it, if any.</summary>
public sealed class VisibilityAnimEntry
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("base")] public bool Base { get; set; }
    [JsonPropertyName("curve")] public VisibilityAnimCurve? Curve { get; set; }

    /// <summary>Whether the name is drawn at the frame.</summary>
    public bool VisibleAt(float frame) => Curve is null ? Base : Curve.Evaluate(frame) != 0;
}

/// <summary>A stepped or baked 0/1 curve, as the loader evaluates integer curves.</summary>
public sealed class VisibilityAnimCurve
{
    [JsonPropertyName("curve_type")] public int CurveType { get; set; }
    [JsonPropertyName("start_frame")] public float StartFrame { get; set; }
    [JsonPropertyName("end_frame")] public float EndFrame { get; set; }
    [JsonPropertyName("offset")] public float Offset { get; set; }
    [JsonPropertyName("frames")] public float[] Frames { get; set; } = [];
    [JsonPropertyName("keys")] public float[][] Keys { get; set; } = [];

    public int Evaluate(float frame) => AnimCurveEval.EvaluateInt(StartFrame, EndFrame, (int)Offset, Frames, Keys, frame);
}
