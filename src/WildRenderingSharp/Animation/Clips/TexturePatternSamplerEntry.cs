using System.Text.Json.Serialization;

namespace WildRenderingSharp.Animation.Clips;

/// <summary>Animation data for which texture to use on which frames.</summary>
public sealed class TexturePatternSamplerEntry
{
    [JsonPropertyName("sampler")] public string Sampler { get; set; } = "";
    [JsonPropertyName("start_frame")] public float StartFrame { get; set; }
    [JsonPropertyName("end_frame")] public float EndFrame { get; set; }
    [JsonPropertyName("frames")] public float[] Frames { get; set; } = [];
    
    // Indices into the set of textures available to the animation
    [JsonPropertyName("values")] public int[] Values { get; set; } = [];

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
