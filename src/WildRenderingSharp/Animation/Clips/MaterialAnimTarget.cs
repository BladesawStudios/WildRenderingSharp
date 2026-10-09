using System.Text.Json.Serialization;
using WildRenderingSharp.Animation.Posing;

namespace WildRenderingSharp.Animation.Clips;

/// <summary>
/// One thing a material anim writes: four bytes at <c>byte_offset</c> within the shader parameter <c>param</c>, either from a curve
/// or as a fixed value.
/// </summary>
public sealed class MaterialAnimTarget
{
    [JsonPropertyName("param")] public string Param { get; set; } = "";
    // The BFRES ShaderParamType name, for display - the evaluation never needs it (a curve's own type says whether it produces
    // int or float bits, and a constant is raw bits either way).
    [JsonPropertyName("param_type")] public string ParamType { get; set; } = "";
    /// <summary>Byte offset WITHIN the parameter (<c>AnimCurve.AnimDataOffset</c>): 0 for a plain float, 20 for a TexSrt's translate-Y, and so on.</summary>
    [JsonPropertyName("byte_offset")] public int ByteOffset { get; set; }

    /// <summary>Set on a constant target - the raw 32 bits to store, needing no float/int interpretation because the shader's own reading of that byte decides.</summary>
    [JsonPropertyName("constant_bits")] public uint? ConstantBits { get; set; }

    [JsonPropertyName("is_int")] public bool IsInt { get; set; }
    [JsonPropertyName("curve_type")] public int CurveType { get; set; }
    [JsonPropertyName("start_frame")] public float StartFrame { get; set; }
    [JsonPropertyName("end_frame")] public float EndFrame { get; set; }
    [JsonPropertyName("scale")] public float Scale { get; set; } = 1f;
    /// <summary>The curve's base value. For an int curve this is its Int32 view (exported as a whole number); <see cref="Bits"/> reads it accordingly.</summary>
    [JsonPropertyName("offset")] public float Offset { get; set; }
    [JsonPropertyName("frames")] public float[] Frames { get; set; } = [];
    [JsonPropertyName("keys")] public float[][] Keys { get; set; } = [];

    public uint Bits(float frame)
    {
        if (ConstantBits is { } bits)
            return bits;
        if (IsInt)
            return unchecked((uint)AnimCurveEval.EvaluateInt(StartFrame, EndFrame, (int)Offset, Frames, Keys, frame));
        return BitConverter.SingleToUInt32Bits(
            AnimCurveEval.EvaluateFloat(CurveType, StartFrame, EndFrame, Scale, Offset, Frames, Keys, frame));
    }
}
