using System.Text.Json;
using System.Text.Json.Serialization;
using WildRenderingSharp.Assets.Manifests;

namespace WildRenderingSharp.Animation.Clips;

/// <summary>
/// Deserialized <c>&lt;Model&gt;.&lt;AnimName&gt;.anim.json</c>, written by
/// <c>ShaderLibrary.CompileTool.ExportTestBench.ExportSkeletalAnim</c> from a <c>BfresLibrary.SkeletalAnim</c> (FSKA).
/// </summary>
public sealed class SkeletalAnimManifest : IAnimClip
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("frame_count")] public int FrameCount { get; set; }
    [JsonPropertyName("loop")] public bool Loop { get; set; }
    // SkeletalAnim.FlagsRotate. nn::g3d2::SkeletalAnimObj::ApplyTo (Ghidra 0x710007a93c) dispatches on (FSKA.flags >> 12) & 7:
    // 0 takes the quaternion path, 1 takes ApplyToImpl<nn::g3d::EulerToMtx>. When this is false, BaseRotate and curve target
    // offsets 32/36/40 are Euler XYZ RADIANS and the W slot is unused - reading them as a quaternion produces a normalized
    // garbage rotation, not a slightly-off one.
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

    public static IEnumerable<string> ListAvailable(string dataDirectory, string modelName) =>
        Directory.Exists(dataDirectory)
            ? Directory.EnumerateFiles(dataDirectory, $"{modelName}.*.anim.json")
                .Select(Path.GetFileName)
                .Select(n => n![(modelName.Length + 1)..^".anim.json".Length])
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            : [];
}
