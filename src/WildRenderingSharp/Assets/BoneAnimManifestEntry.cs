using System.Text.Json.Serialization;

namespace WildRenderingSharp.Assets;

/// <summary>One bone's animation from a <c>&lt;Model&gt;.&lt;AnimName&gt;.anim.json</c> file.</summary>
public sealed class BoneAnimManifestEntry
{
    [JsonPropertyName("bone_name")] public string BoneName { get; set; } = "";
    [JsonPropertyName("use_scale")] public bool UseScale { get; set; }
    [JsonPropertyName("use_rotate")] public bool UseRotate { get; set; }
    [JsonPropertyName("use_translate")] public bool UseTranslate { get; set; }
    /// <summary><c>BoneAnim.ApplySegmentScaleCompensate</c>. The game lets an anim override bone flag bits 23-27 wholesale (<c>ApplyToImpl</c> writes <c>(animFlags &gt;&gt; 23 &amp; 0x1F) &lt;&lt; 23</c> into the bone's local-matrix flags), so an animated bone takes its segment-scale-compensate state from here rather than from the skeleton.</summary>
    [JsonPropertyName("segment_scale_compensate")] public bool SegmentScaleCompensate { get; set; }
    /// <summary>Default/initial (Scale.xyz, Translate.xyz, Rotate.xyzw), read from <c>BoneAnim.BaseData</c> - the value a curve-less component holds. Only meaningful for a component whose matching <c>use_*</c> flag is set; the others are written as zero, not as an identity.</summary>
    [JsonPropertyName("base_scale")] public float[] BaseScale { get; set; } = [1, 1, 1];
    [JsonPropertyName("base_translate")] public float[] BaseTranslate { get; set; } = [0, 0, 0];
    /// <summary>Quaternion (x, y, z, w) if the anim's <see cref="SkeletalAnimManifest.RotationIsQuaternion"/> is set, else Euler XYZ radians in (x, y, z, unused).</summary>
    [JsonPropertyName("base_rotate")] public float[] BaseRotate { get; set; } = [0, 0, 0, 1];
    [JsonPropertyName("curves")] public List<AnimCurveManifestEntry> Curves { get; set; } = [];
}
