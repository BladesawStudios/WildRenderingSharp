using System.Text.Json.Serialization;

namespace WildRenderingSharp.Animation.Clips;

// TODO: Rename to BoneAnimation or something like that -- torf
/// <summary>
/// A full animation for a single bone, potentially composed of several curves.
/// Each curve separately animates different translation/rotation/scale components.
/// Loaded from <c>&lt;Model&gt;.&lt;AnimName&gt;.anim.json</c> in the cache.
/// Corresponds to a BFRES bone animation (the linked wiki may not match TOTK's format exactly):
/// https://wiki.wexosmk.xyz/index.php/BFRES_(File_Format)/Switch#Bone_Animation
/// </summary>
public sealed class BoneAnimManifestEntry
{
    [JsonPropertyName("bone_name")] public string BoneName { get; set; } = "";
    [JsonPropertyName("use_scale")] public bool UseScale { get; set; }
    [JsonPropertyName("use_rotate")] public bool UseRotate { get; set; }
    [JsonPropertyName("use_translate")] public bool UseTranslate { get; set; }
    // An animated bone takes its segment-scale-compensate state (flag bits 23-27) from the anim rather than the skeleton.
    [JsonPropertyName("segment_scale_compensate")] public bool SegmentScaleCompensate { get; set; }
    
    // The initial scale/translation/rotation values used if no curve outputs to
    // a component.
    [JsonPropertyName("base_scale")] public float[] BaseScale { get; set; } = [1, 1, 1];
    [JsonPropertyName("base_translate")] public float[] BaseTranslate { get; set; } = [0, 0, 0];
    [JsonPropertyName("base_rotate")] public float[] BaseRotate { get; set; } = [0, 0, 0, 1];
    [JsonPropertyName("curves")] public List<AnimCurveManifestEntry> Curves { get; set; } = [];
}
