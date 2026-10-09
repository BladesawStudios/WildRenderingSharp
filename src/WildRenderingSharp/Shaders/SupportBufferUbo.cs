namespace WildRenderingSharp.Shaders;

/// <summary>
/// The decompiler's own uniform block at binding 0, which every decompiled shader declares whatever game it came from. It holds
/// <c>alpha_test</c>, <c>is_bgra[8]</c>, the viewport and a 73-entry <c>render_scale</c> array.
/// </summary>
public static class SupportBufferUbo
{
    public const int ByteSize = 512;
    public const uint BindingIndex = 0;

    public static byte[] Build()
    {
        var bytes = new byte[ByteSize];
        BitConverter.GetBytes(1.0f).CopyTo(bytes, 36);
        for (int i = 0; i < 73; i++)
            BitConverter.GetBytes(1.0f).CopyTo(bytes, 56 + i * 4);
        return bytes;
    }
}
