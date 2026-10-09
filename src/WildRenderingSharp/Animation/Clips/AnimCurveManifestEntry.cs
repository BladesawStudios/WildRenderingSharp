using System.Text.Json.Serialization;

namespace WildRenderingSharp.Animation.Clips;

/// <summary>One curve from a <see cref="BoneAnimManifestEntry"/>, as BfresLibrary decoded it, with <see cref="TargetOffset"/> saying which TRS component it drives.</summary>
public sealed class AnimCurveManifestEntry
{
    // 0 = Cubic, 16 = Linear, 32 = BakedFloat (the only three SkeletonPose evaluates - the others are integer/bool curve types
    // no bone TRS component uses).
    [JsonPropertyName("curve_type")] public int CurveType { get; set; }
    // The TRS component: 4/8/12 = scale XYZ, 16/20/24 = translate XYZ, 32/36/40/44 = rotate XYZW.
    [JsonPropertyName("target_offset")] public int TargetOffset { get; set; }
    [JsonPropertyName("start_frame")] public float StartFrame { get; set; }
    [JsonPropertyName("end_frame")] public float EndFrame { get; set; }
    [JsonPropertyName("scale")] public float Scale { get; set; } = 1f;
    // The curve's BASE value, AnimCurve.Offset at ResAnimCurve[0x24]: nn::g3d2::ResAnimCurve::EvaluateFloat (Ghidra 0x7100073d90) finishes every
    // curve with Offset + raw * Scale. Not to be confused with Delta.
    [JsonPropertyName("offset")] public float Offset { get; set; }
    // AnimCurve.Delta (ResAnimCurve[0x28]) is the per-loop increment only the relative-repeat wrap mode adds, and 0 on essentially every curve. It
    // plays no part in normal in-range evaluation, which uses Offset.
    [JsonPropertyName("delta")] public float Delta { get; set; }
    // Wrap mode before StartFrame (0 = Clamp, 1 = Repeat, 2 = Mirror). SkeletonPose clamps regardless, which only differs
    // outside the anim's own frame range.
    [JsonPropertyName("pre_wrap")] public int PreWrap { get; set; }
    [JsonPropertyName("post_wrap")] public int PostWrap { get; set; }
    [JsonPropertyName("frames")] public float[] Frames { get; set; } = [];
    [JsonPropertyName("keys")] public float[][] Keys { get; set; } = [];
}
