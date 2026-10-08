namespace WildRenderingSharp.Profiles.Totk.Shaders;

/// <summary>
/// Ryujinx's decompiler "support buffer", binding 0 - not a game/gsys concept at all, an artifact of running Ryujinx's
/// Maxwell-to-GLSL decompiled shaders on desktop GL in the first place (its ABI reserves this block for <c>alpha_test</c>,
/// <c>is_bgra[8]</c>, viewport inverse/size, then a 73-entry <c>render_scale</c> array). It belongs with the pipeline, not
/// <c>WildRenderingSharp.Shaders</c>' per-game UBO profile. WildRenderingSharp always renders at native resolution with alpha test
/// disabled at this level (alpha cutout is instead a real <c>discard</c> in the shader, driven by the material's own UBO), so every
/// field here is a constant: alpha_test off, and <c>render_scale</c> all 1.0 (no dynamic resolution scaling).
/// </summary>
public static class SupportBufferUbo
{
    public const int ByteSize = 512;
    public const uint BindingIndex = Profiles.Totk.TotkBindings.Support;

    public static byte[] Build()
    {
        var bytes = new byte[ByteSize];
        BitConverter.GetBytes(1.0f).CopyTo(bytes, 36);
        for (int i = 0; i < 73; i++)
            BitConverter.GetBytes(1.0f).CopyTo(bytes, 56 + i * 4);
        return bytes;
    }
}
