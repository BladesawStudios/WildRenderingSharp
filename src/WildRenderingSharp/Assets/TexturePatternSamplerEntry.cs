using System.Text.Json.Serialization;

namespace WildRenderingSharp.Assets;

/// <summary>
/// One sampler of one material, and the step curve of texture indices driving it. <see cref="Frames"/> and <see cref="Values"/> are
/// parallel; an entry with no curve at all (a constant selection for the whole anim) exports an empty <see cref="Frames"/> and a
/// single <see cref="Values"/> element, so <see cref="Evaluate"/> needs no separate case for it.
/// </summary>
public sealed class TexturePatternSamplerEntry
{
    [JsonPropertyName("sampler")] public string Sampler { get; set; } = "";
    [JsonPropertyName("start_frame")] public float StartFrame { get; set; }
    [JsonPropertyName("end_frame")] public float EndFrame { get; set; }
    [JsonPropertyName("frames")] public float[] Frames { get; set; } = [];
    /// <summary>Index into the anim's own <see cref="TexturePatternAnimManifest.Textures"/>, already including the curve's integer <c>Offset</c>.</summary>
    [JsonPropertyName("values")] public int[] Values { get; set; } = [];

    /// <summary>
    /// The texture index at <paramref name="frame"/>. STEP, not interpolated: the value of the last key at or before the frame is
    /// held until the next key.
    /// </summary>
    public int Evaluate(float frame)
    {
        if (Values.Length == 0)
            return -1;
        if (Frames.Length == 0)
            return Values[0];

        float f = Math.Clamp(frame, StartFrame, EndFrame);
        int i = 0;
        while (i + 1 < Frames.Length && Frames[i + 1] <= f)
            i++;
        return Values[Math.Min(i, Values.Length - 1)];
    }
}
