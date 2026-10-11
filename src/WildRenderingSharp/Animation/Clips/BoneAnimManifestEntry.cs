using System.Text.Json.Serialization;

namespace WildRenderingSharp.Animation.Clips;

/// <summary>One bone's animation from a <c>&lt;Model&gt;.&lt;AnimName&gt;.anim.json</c> file.</summary>
public sealed class BoneAnimManifestEntry
{
    [JsonPropertyName("bone_name")] public string BoneName { get; set; } = "";
    [JsonPropertyName("use_scale")] public bool UseScale { get; set; }
    [JsonPropertyName("use_rotate")] public bool UseRotate { get; set; }
    [JsonPropertyName("use_translate")] public bool UseTranslate { get; set; }
    // An animated bone takes its segment-scale-compensate state (flag bits 23-27) from the anim rather than the skeleton.
    [JsonPropertyName("segment_scale_compensate")] public bool SegmentScaleCompensate { get; set; }
    // The value a curve-less component holds (scale, translate, rotate); zero, not identity, for a component without its use_* flag.
    [JsonPropertyName("base_scale")] public float[] BaseScale { get; set; } = [1, 1, 1];
    [JsonPropertyName("base_translate")] public float[] BaseTranslate { get; set; } = [0, 0, 0];
    [JsonPropertyName("base_rotate")] public float[] BaseRotate { get; set; } = [0, 0, 0, 1];
    [JsonPropertyName("curves")] public List<AnimCurveManifestEntry> Curves { get; set; } = [];
}
