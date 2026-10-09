using System.Text.Json.Serialization;

namespace WildRenderingSharp.Animation.Clips;

/// <summary>One bone's animation from a <c>&lt;Model&gt;.&lt;AnimName&gt;.anim.json</c> file.</summary>
public sealed class BoneAnimManifestEntry
{
    [JsonPropertyName("bone_name")] public string BoneName { get; set; } = "";
    [JsonPropertyName("use_scale")] public bool UseScale { get; set; }
    [JsonPropertyName("use_rotate")] public bool UseRotate { get; set; }
    [JsonPropertyName("use_translate")] public bool UseTranslate { get; set; }
    // BoneAnim.ApplySegmentScaleCompensate. The game lets an anim override bone flag bits 23-27 wholesale (ApplyToImpl writes
    // (animFlags >> 23 & 0x1F) << 23 into the bone's local-matrix flags), so an animated bone takes its
    // segment-scale-compensate state from here rather than from the skeleton.
    [JsonPropertyName("segment_scale_compensate")] public bool SegmentScaleCompensate { get; set; }
    // Default/initial (Scale.xyz, Translate.xyz, Rotate.xyzw), read from BoneAnim.BaseData - the value a curve-less component
    // holds. Only meaningful for a component whose matching use_* flag is set; the others are written as zero, not as an
    // identity.
    [JsonPropertyName("base_scale")] public float[] BaseScale { get; set; } = [1, 1, 1];
    [JsonPropertyName("base_translate")] public float[] BaseTranslate { get; set; } = [0, 0, 0];
    [JsonPropertyName("base_rotate")] public float[] BaseRotate { get; set; } = [0, 0, 0, 1];
    [JsonPropertyName("curves")] public List<AnimCurveManifestEntry> Curves { get; set; } = [];
}
