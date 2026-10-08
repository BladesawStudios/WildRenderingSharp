using System.Text.Json.Serialization;

namespace WildRenderingSharp.Assets;

/// <summary>
/// One curve from a <see cref="BoneAnimManifestEntry"/> - a straight transcription of <c>BfresLibrary.AnimCurve</c>'s
/// already-decoded (float, regardless of on-disk compression) <c>Frames</c>/<c>Keys</c>, plus <see cref="TargetOffset"/>
/// (<c>AnimCurve.AnimDataOffset</c>) to say which component of the bone's TRS this curve drives: 4=ScaleX, 8=ScaleY, 12=ScaleZ,
/// 16=TranslateX, 20=TranslateY, 24=TranslateZ, 32=RotateX, 36=RotateY, 40=RotateZ, 44=RotateW. Those offsets are not guessed -
/// they are the byte offsets <c>nn::g3d2::SkeletalAnimObj::ApplyToImpl</c> (Ghidra 0x710007a1a0) reads out of its per-bone result
/// struct when it copies scale into the local matrix's scale slot, translation into its translation row, and rotation into the
/// Euler/quaternion conversion.
/// </summary>
public sealed class AnimCurveManifestEntry
{
    // 0 = Cubic, 16 = Linear, 32 = BakedFloat (the only three SkeletonPose evaluates - the others are integer/bool curve types
    // no bone TRS component uses).
    [JsonPropertyName("curve_type")] public int CurveType { get; set; }
    [JsonPropertyName("target_offset")] public int TargetOffset { get; set; }
    [JsonPropertyName("start_frame")] public float StartFrame { get; set; }
    [JsonPropertyName("end_frame")] public float EndFrame { get; set; }
    [JsonPropertyName("scale")] public float Scale { get; set; } = 1f;
    // The curve's BASE value - AnimCurve.Offset, the field at ResAnimCurve[0x24]. nn::g3d2::ResAnimCurve::EvaluateFloat (Ghidra
    // 0x7100073d90) finishes every curve with Offset + raw * Scale, where raw is the (usually quantized-integer) polynomial the
    // Cubic/Linear/Baked evaluator produced. Do not confuse this with Delta.
    [JsonPropertyName("offset")] public float Offset { get; set; }
    // AnimCurve.Delta (ResAnimCurve[0x28]) - the per-loop increment that ONLY the relative-repeat wrap mode adds, and 0 on
    // essentially every curve. It is not part of the normal in-range evaluation; Offset is. Carried for completeness.
    [JsonPropertyName("delta")] public float Delta { get; set; }
    // Wrap mode before StartFrame (0 = Clamp, 1 = Repeat, 2 = Mirror). SkeletonPose clamps regardless, which only differs
    // outside the anim's own frame range.
    [JsonPropertyName("pre_wrap")] public int PreWrap { get; set; }
    [JsonPropertyName("post_wrap")] public int PostWrap { get; set; }
    [JsonPropertyName("frames")] public float[] Frames { get; set; } = [];
    [JsonPropertyName("keys")] public float[][] Keys { get; set; } = [];
}
