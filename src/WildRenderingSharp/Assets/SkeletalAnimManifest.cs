using System.Text.Json;
using System.Text.Json.Serialization;

namespace WildRenderingSharp.Assets;

/// <summary>
/// One curve from a <see cref="BoneAnimManifestEntry"/> - a straight transcription of
/// <c>BfresLibrary.AnimCurve</c>'s already-decoded (float, regardless of on-disk compression)
/// <c>Frames</c>/<c>Keys</c>, plus <see cref="TargetOffset"/> (<c>AnimCurve.AnimDataOffset</c>)
/// to say which component of the bone's TRS this curve drives:
/// 4=ScaleX, 8=ScaleY, 12=ScaleZ, 16=TranslateX, 20=TranslateY, 24=TranslateZ,
/// 32=RotateX, 36=RotateY, 40=RotateZ, 44=RotateW.
///
/// Those offsets are not guessed - they are the byte offsets
/// <c>nn::g3d2::SkeletalAnimObj::ApplyToImpl</c> (Ghidra 0x710007a1a0) reads out of its per-bone
/// result struct when it copies scale into the local matrix's scale slot, translation into its
/// translation row, and rotation into the Euler/quaternion conversion.
/// </summary>
public sealed class AnimCurveManifestEntry
{
    /// <summary>0 = Cubic, 16 = Linear, 32 = BakedFloat (the only three <see cref="WildRenderingSharp.Rendering.SkeletonPose"/> evaluates - the others are integer/bool curve types no bone TRS component uses).</summary>
    [JsonPropertyName("curve_type")] public int CurveType { get; set; }
    [JsonPropertyName("target_offset")] public int TargetOffset { get; set; }
    [JsonPropertyName("start_frame")] public float StartFrame { get; set; }
    [JsonPropertyName("end_frame")] public float EndFrame { get; set; }
    [JsonPropertyName("scale")] public float Scale { get; set; } = 1f;
    /// <summary>
    /// The curve's BASE value - <c>AnimCurve.Offset</c>, the field at <c>ResAnimCurve[0x24]</c>.
    /// <c>nn::g3d2::ResAnimCurve::EvaluateFloat</c> (Ghidra 0x7100073d90) finishes every curve
    /// with <c>Offset + raw * Scale</c>, where <c>raw</c> is the (usually quantized-integer)
    /// polynomial the Cubic/Linear/Baked evaluator produced. Do not confuse this with
    /// <see cref="Delta"/>.
    /// </summary>
    [JsonPropertyName("offset")] public float Offset { get; set; }
    /// <summary>
    /// <c>AnimCurve.Delta</c> (<c>ResAnimCurve[0x28]</c>) - the per-loop increment that ONLY the
    /// relative-repeat wrap mode adds, and 0 on essentially every curve. It is not part of the
    /// normal in-range evaluation; <see cref="Offset"/> is. Carried for completeness.
    /// </summary>
    [JsonPropertyName("delta")] public float Delta { get; set; }
    /// <summary>Wrap mode before <see cref="StartFrame"/> (0 = Clamp, 1 = Repeat, 2 = Mirror). <see cref="WildRenderingSharp.Rendering.SkeletonPose"/> clamps regardless, which only differs outside the anim's own frame range.</summary>
    [JsonPropertyName("pre_wrap")] public int PreWrap { get; set; }
    /// <summary>Wrap mode after <see cref="EndFrame"/>; see <see cref="PreWrap"/>.</summary>
    [JsonPropertyName("post_wrap")] public int PostWrap { get; set; }
    /// <summary>Keyframe times. Empty for BakedFloat, which samples one value per integer frame in [StartFrame, EndFrame] instead.</summary>
    [JsonPropertyName("frames")] public float[] Frames { get; set; } = [];
    /// <summary>Jagged [key][element]: 4 floats/key for Cubic, 2 for Linear, 1 per integer frame for BakedFloat.</summary>
    [JsonPropertyName("keys")] public float[][] Keys { get; set; } = [];
}

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

/// <summary>
/// Deserialized <c>&lt;Model&gt;.&lt;AnimName&gt;.anim.json</c>, written by
/// <c>ShaderLibrary.CompileTool.ExportTestBench.ExportSkeletalAnim</c> from a
/// <c>BfresLibrary.SkeletalAnim</c> (FSKA).
/// </summary>
public sealed class SkeletalAnimManifest : IAnimClip
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("frame_count")] public int FrameCount { get; set; }
    [JsonPropertyName("loop")] public bool Loop { get; set; }
    /// <summary>
    /// <c>SkeletalAnim.FlagsRotate</c>. <c>nn::g3d2::SkeletalAnimObj::ApplyTo</c> (Ghidra
    /// 0x710007a93c) dispatches on <c>(FSKA.flags &gt;&gt; 12) &amp; 7</c>: 0 takes the quaternion
    /// path, 1 takes <c>ApplyToImpl&lt;nn::g3d::EulerToMtx&gt;</c>. When this is false,
    /// <see cref="BoneAnimManifestEntry.BaseRotate"/> and curve target offsets 32/36/40 are Euler
    /// XYZ RADIANS and the W slot is unused - reading them as a quaternion produces a normalized
    /// garbage rotation, not a slightly-off one.
    ///
    /// Defaults to true only so an anim manifest exported before this field existed keeps its old
    /// behaviour; every such file should be re-exported.
    /// </summary>
    [JsonPropertyName("rotation_is_quaternion")] public bool RotationIsQuaternion { get; set; } = true;
    /// <summary><c>SkeletalAnim.FlagsScale</c>, same encoding as <see cref="SkeletonScalingMode"/>. Carried for completeness; the hierarchy walk uses the SKELETON's mode, which is what <c>SkeletonObj::CalculateWorldMtx</c> reads.</summary>
    [JsonPropertyName("scaling_mode")] public SkeletonScalingMode ScalingMode { get; set; } = SkeletonScalingMode.Standard;
    [JsonPropertyName("bone_anims")] public List<BoneAnimManifestEntry> BoneAnims { get; set; } = [];

    static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static SkeletalAnimManifest Load(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<SkeletalAnimManifest>(stream, JsonOptions)
            ?? throw new InvalidDataException($"'{path}' did not deserialize to a skeletal anim manifest.");
    }

    /// <summary>Every embedded anim's name next to a model's manifest (<c>&lt;modelName&gt;.&lt;AnimName&gt;.anim.json</c>).</summary>
    public static IEnumerable<string> ListAvailable(string dataDirectory, string modelName) =>
        Directory.Exists(dataDirectory)
            ? Directory.EnumerateFiles(dataDirectory, $"{modelName}.*.anim.json")
                .Select(Path.GetFileName)
                .Select(n => n![(modelName.Length + 1)..^".anim.json".Length])
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            : [];
}
