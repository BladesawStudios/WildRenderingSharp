using System.Text.Json;
using System.Text.Json.Serialization;
using WildRenderingSharp.Assets.Manifests;

namespace WildRenderingSharp.Animation.Clips;

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

    static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static SkeletalAnimManifest Load(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<SkeletalAnimManifest>(stream, JsonOptions)
            ?? throw new InvalidDataException($"'{path}' did not deserialize to a skeletal anim manifest.");
    }

    public static IEnumerable<string> ListAvailable(string dataDirectory, string modelName) =>
        Directory.Exists(dataDirectory)
            ? Directory.EnumerateFiles(dataDirectory, $"{modelName}.*.anim.json")
                .Select(Path.GetFileName)
                .Select(n => n![(modelName.Length + 1)..^".anim.json".Length])
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            : [];
}
