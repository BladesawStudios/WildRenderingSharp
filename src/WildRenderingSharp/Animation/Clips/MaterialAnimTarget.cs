using System.Text.Json.Serialization;
using WildRenderingSharp.Animation.Posing;

namespace WildRenderingSharp.Animation.Clips;

// TODO: Rename this to MaterialAnimCurve or something like that -- torf
/// <summary>
/// Everything needed to evaluate & output the result of a material animation curve.
/// The result is output directly to a shader parameter buffer, which can be a
/// float or a 32-bit int.
/// </summary>
public sealed class MaterialAnimTarget
{
    [JsonPropertyName("param")] public string Param { get; set; } = "";
    // The ShaderParamType name from the BFRES. Only used for display.
    [JsonPropertyName("param_type")] public string ParamType { get; set; } = "";
    // Destination offset in the shader parameter buffer
    [JsonPropertyName("byte_offset")] public int ByteOffset { get; set; }

    // Used for "curves" that just output a constant value.
    // Interpretation is determined by the shader (it may be float or int data)
    [JsonPropertyName("constant_bits")] public uint? ConstantBits { get; set; }

    [JsonPropertyName("is_int")] public bool IsInt { get; set; }
    
    // TODO: Make this an enum.
    /// <summary>
    /// See constants in the AnimCurveEval class.
    /// </summary>
    [JsonPropertyName("curve_type")] public int CurveType { get; set; }
    [JsonPropertyName("start_frame")] public float StartFrame { get; set; }
    [JsonPropertyName("end_frame")] public float EndFrame { get; set; }
    [JsonPropertyName("scale")] public float Scale { get; set; } = 1f;
    
    // Added to the final scaled curve result (cast to int for int curves)
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
