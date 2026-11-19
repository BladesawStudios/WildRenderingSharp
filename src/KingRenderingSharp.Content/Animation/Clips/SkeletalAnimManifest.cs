using System.Text.Json;
using System.Text.Json.Serialization;
using KingRenderingSharp.Content.Assets.Manifests;
using KingRenderingSharp.Core.Storage;

namespace KingRenderingSharp.Content.Animation.Clips;

/// <summary>
/// A full skeletal animation.
/// Corresponds to a BFRES FSKA, loaded from
/// <c>&lt;Model&gt;.&lt;AnimName&gt;.anim.json</c> in the cache.
/// </summary>
public sealed class SkeletalAnimManifest : IAnimClip
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("frame_count")] public int FrameCount { get; set; }
    [JsonPropertyName("loop")] public bool Loop { get; set; }
    
    // If this is false, rotation values are Euler XYZ (radians) with W being unused.
    [JsonPropertyName("rotation_is_quaternion")] public bool RotationIsQuaternion { get; set; } = true;
    
    // How bone scaling is applied throughout the bone hierarchy.
    // In practice this is unused and just gets read from the skeleton itself.
    [JsonPropertyName("scaling_mode")] public SkeletonScalingMode ScalingMode { get; set; } = SkeletonScalingMode.Standard;
    
    // All animation curves for all bones being animated.
    [JsonPropertyName("bone_anims")] public List<BoneAnimManifestEntry> BoneAnims { get; set; } = [];



}
