using System.Text.Json.Serialization;

namespace WildRenderingSharp.Animation.Clips;

// TODO: Rename this BoneAnimCurve or something like that -- torf
/// <summary>
/// An animation curve to be applied to a bone.
/// Only outputs to a single translation/rotation/scale component.
/// </summary>
public sealed class AnimCurveManifestEntry
{
    // TODO: Make this an enum.
    /// <summary>
    /// See constants in the AnimCurveEval class.
    /// </summary>
    [JsonPropertyName("curve_type")] public int CurveType { get; set; }
    
    /// <summary>
    /// Offsets in the bone's transform data to output to. Common ones are:
    /// 4/8/12 = scale XYZ, 16/20/24 = translate XYZ, 32/36/40/44 = rotate XYZW.
    /// </summary>
    /// <remarks>
    /// TODO: Online wikis seem to imply the BFRES allows a single curve to
    /// target several components via a bitfield. Check the game files and
    /// BfresLibrary to see if this is still the case and how it's handled.
    /// https://wiki.wexosmk.xyz/index.php/BFRES_(File_Format)/Switch#Bone_Animation
    /// </remarks>
    [JsonPropertyName("target_offset")] public int TargetOffset { get; set; }
    [JsonPropertyName("start_frame")] public float StartFrame { get; set; }
    [JsonPropertyName("end_frame")] public float EndFrame { get; set; }
    [JsonPropertyName("scale")] public float Scale { get; set; } = 1f;
    
    // Added to the final scaled curve result
    [JsonPropertyName("offset")] public float Offset { get; set; }
    
    // Appears to be used to extrapolate results outside the normal range of the
    // animation. Not entirely clear, please check in-game or in Ghidra.
    [JsonPropertyName("delta")] public float Delta { get; set; }
    
    // TODO: Make this an enum.
    // Wrap mode before StartFrame (0 = Clamp, 1 = Repeat, 2 = Mirror). SkeletonPose clamps regardless, which only differs
    // outside the anim's own frame range.
    [JsonPropertyName("pre_wrap")] public int PreWrap { get; set; }
    [JsonPropertyName("post_wrap")] public int PostWrap { get; set; }
    [JsonPropertyName("frames")] public float[] Frames { get; set; } = [];
    [JsonPropertyName("keys")] public float[][] Keys { get; set; } = [];
}
